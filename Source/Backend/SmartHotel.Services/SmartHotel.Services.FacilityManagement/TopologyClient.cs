﻿using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SmartHotel.Services.FacilityManagement.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SmartHotel.Services.FacilityManagement
{
	public interface ITopologyClient
	{
		string AccessToken { get; set; }
		Task<ICollection<Space>> GetSpaces();
	}

	public class TopologyClient : ITopologyClient
	{
		private readonly Dictionary<int, string> _typesById = new Dictionary<int, string>();
		private const string TenantTypeName = "Tenant";
		private const string HotelBrandTypeName = "HotelBrand";
		private const string HotelTypeName = "Venue";
		private const string FloorTypeName = "Floor";
		private const string RoomTypeName = "Room";
		private readonly Dictionary<string, int> _typeIdsByName = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase )
		{
			{TenantTypeName, int.MinValue},
			{HotelBrandTypeName, int.MinValue},
			{HotelTypeName, int.MinValue},
			{FloorTypeName, int.MinValue},
			{RoomTypeName, int.MinValue},
		};
		private readonly string ApiPath = "api/v1.0/";

		private readonly string SpacesPath = "spaces";
		private readonly string TypesPath = "types";

		private readonly string SpacesFilter = "";

		private readonly string TypesFilter =
			$"names={TenantTypeName};{HotelBrandTypeName};{HotelTypeName};{FloorTypeName};{RoomTypeName}&categories=SpaceType";

		private readonly IHttpClientFactory _clientFactory;
		private readonly IConfiguration _config;

		public TopologyClient( IConfiguration config, IHttpClientFactory clientFactory )
		{
			_clientFactory = clientFactory;
			_config = config;
		}

		public string AccessToken { get; set; }
		public async Task<ICollection<Space>> GetSpaces()
		{
			var httpClient = _clientFactory.CreateClient();
			string managementBaseUrl = _config["ManagementApiUrl"];
			string protectedManagementBaseUrl = managementBaseUrl.EndsWith( '/' ) ? managementBaseUrl : $"{managementBaseUrl}/";
			httpClient.BaseAddress = new Uri( protectedManagementBaseUrl );
			httpClient.DefaultRequestHeaders.Add( "Authorization", $"Bearer {AccessToken}" );

			await GetAndUpdateTypeIds( httpClient );

			var response = await GetFromDigitalTwins( httpClient, $"{ApiPath}{SpacesPath}?{SpacesFilter}" );
			var topology = JsonConvert.DeserializeObject<ICollection<DigitalTwinsSpace>>( response );

			Space tenantSpace = null;
			Space hotelBrandSpace = null;
			Space hotelSpace = null;
			Space floorSpace = null;

			var spacesByParentId = new Dictionary<string, List<Space>>();
			foreach ( DigitalTwinsSpace dtSpace in topology )
			{
				if ( _typesById.TryGetValue( dtSpace.typeId, out string typeName ) )
				{
					var space = new Space
					{
						Id = dtSpace.id,
						Name = dtSpace.name,
						FriendlyName = dtSpace.friendlyName,
						Type = typeName,
						TypeId = dtSpace.typeId,
						ParentSpaceId = dtSpace.parentSpaceId ?? string.Empty
					};

					if ( tenantSpace == null && TenantTypeName.Equals( typeName, StringComparison.OrdinalIgnoreCase ) )
					{
						tenantSpace = space;
					}
					else if ( tenantSpace == null
							 && hotelBrandSpace == null
							 && HotelBrandTypeName.Equals( typeName, StringComparison.OrdinalIgnoreCase ) )
					{
						hotelBrandSpace = space;
					}
					else if ( tenantSpace == null
							 && hotelBrandSpace == null
							 && hotelSpace == null
							 && HotelTypeName.Equals( typeName, StringComparison.OrdinalIgnoreCase ) )
					{
						hotelSpace = space;
					}
					else if ( tenantSpace == null
							  && hotelBrandSpace == null
							  && hotelSpace == null
							  && floorSpace == null
							  && FloorTypeName.Equals( typeName, StringComparison.OrdinalIgnoreCase ) )
					{
						floorSpace = space;
					}

					if ( !spacesByParentId.TryGetValue( space.ParentSpaceId, out List<Space> spaces ) )
					{
						spaces = new List<Space>();
						spacesByParentId.Add( space.ParentSpaceId, spaces );
					}

					spaces.Add( space );
				}
			}

			var hierarchicalSpaces = new List<Space>();
			Space highestLevelSpace = GetHighestLevelSpace( tenantSpace, hotelBrandSpace, hotelSpace, floorSpace );
			if ( highestLevelSpace != null )
			{
				string highestLevelParentSpaceId = highestLevelSpace.ParentSpaceId;
				hierarchicalSpaces.AddRange( spacesByParentId[highestLevelParentSpaceId] );
				BuildSpaceHierarchyAndReturnRoomSpaces( hierarchicalSpaces, spacesByParentId );
			}

			if ( hierarchicalSpaces.Count == 1 && !FloorTypeName.Equals( hierarchicalSpaces[0].Type, StringComparison.OrdinalIgnoreCase ) )
			{
				// If there is only one root space, then ensuring we only send the child spaces to the client so it knows
				// to start showing those children.
				// TODO: remove the .Where statement. This is only there because of current testing
				hierarchicalSpaces = hierarchicalSpaces[0].ChildSpaces
					.Where( s => !TenantTypeName.Equals( s.Type, StringComparison.OrdinalIgnoreCase ) ).ToList();
			}

			return hierarchicalSpaces;
		}

		private static void BuildSpaceHierarchyAndReturnRoomSpaces( List<Space> hierarchicalSpaces, Dictionary<string, List<Space>> allSpacesByParentId )
		{
			//var roomSpaces = new List<Space>();
			foreach ( Space parentSpace in hierarchicalSpaces )
			{
				if ( allSpacesByParentId.TryGetValue( parentSpace.Id, out List<Space> childSpaces ) )
				{
					parentSpace.ChildSpaces.AddRange( childSpaces );
					BuildSpaceHierarchyAndReturnRoomSpaces( childSpaces, allSpacesByParentId );
				}
			}
		}

		private Space GetHighestLevelSpace( Space tenantSpace, Space hotelBrandSpace, Space hotelSpace, Space floorSpace )
		{
			if ( tenantSpace != null )
			{
				return tenantSpace;
			}

			if ( hotelBrandSpace != null )
			{
				return hotelBrandSpace;
			}

			if ( hotelSpace != null )
			{
				return hotelSpace;
			}

			if ( floorSpace != null )
			{
				return floorSpace;
			}

			return null;
		}

		private async Task GetAndUpdateTypeIds( HttpClient httpClient )
		{
			string typesResponse = await GetFromDigitalTwins( httpClient, $"{ApiPath}{TypesPath}?{TypesFilter}" );
			IReadOnlyCollection<DigitalTwinsType> types = JsonConvert.DeserializeObject<IReadOnlyCollection<DigitalTwinsType>>( typesResponse );

			foreach ( DigitalTwinsType type in types )
			{
				_typesById[type.id] = type.name;
				_typeIdsByName[type.name] = type.id;
			}

			var typesMissingId = _typeIdsByName.Where( kvp => int.MinValue.Equals( kvp.Value ) ).ToArray();
			if ( typesMissingId.Length > 0 )
			{
				throw new NotSupportedException( $"Missing the following type Ids: {string.Join( ", ", typesMissingId )}" );
			}
		}

		private async Task<string> GetFromDigitalTwins( HttpClient httpClient, string requestUri )
		{
			HttpResponseMessage httpResponse = await httpClient.GetAsync( requestUri );
			string content = await httpResponse.Content.ReadAsStringAsync();
			if ( !httpResponse.IsSuccessStatusCode )
			{
				throw new Exception( $"Error when calling Digital Twins with request ({requestUri}): {content}" );
			}

			return content;
		}
	}
}