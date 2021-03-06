import { Component, OnInit, OnDestroy } from '@angular/core';
import { AdalService } from 'adal-angular4';
import { ActivatedRoute, Router } from '@angular/router';
import { environment } from 'src/environments/environment';
import { FormGroup, FormBuilder, Validators } from '@angular/forms';
import { FacilityService } from '../services/facility.service';
import { AppComponent } from '../app.component';
import { BusyService } from '../services/busy.service';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css']
})
export class LoginComponent implements OnInit, OnDestroy {

  private returnUrl: string;

  constructor(
    private formBuilder: FormBuilder,
    private route: ActivatedRoute,
    private router: Router,
    private adalSvc: AdalService,
    private facilityService: FacilityService,
    private busyService: BusyService) { }

  public useBasicAuth = false;
  public submitted = false;
  public loginForm: FormGroup;
  public authenticating = false;
  public invalidAuth = false;

  ngOnInit() {
    document.body.classList.add('white-background');
    document.getElementById('main-nav-container').classList.add('white-background');
    this.returnUrl = this.route.snapshot.queryParams['returnUrl'] || '/';
    if (this.adalSvc.userInfo.authenticated) {
      this.router.navigate([this.returnUrl]);
    } else {
      this.useBasicAuth = environment.useBasicAuth;

      if (this.useBasicAuth) {
        this.loginForm = this.formBuilder.group({
          username: ['', Validators.required],
          password: ['', Validators.required]
        });
      }
    }
  }

  ngOnDestroy() {
    document.body.classList.remove('white-background');
    document.getElementById('main-nav-container').classList.remove('white-background');
  }

  // convenience getter for easy access to form fields
  get f() { return this.loginForm.controls; }

  login() {
    if (this.useBasicAuth) {
      this.submitted = true;
      if (this.loginForm.invalid) {
        return;
      }
      this.invalidAuth = false;
      this.authenticating = true;
      const username = this.f.username.value;
      const password = this.f.password.value;
      const encodedUsernamePassword = window.btoa(`${username}:${password}`);
      const basicAuthData = `Basic ${encodedUsernamePassword}`;
      this.busyService.busy();
      this.facilityService.basicAuthLogin(basicAuthData)
        .subscribe(() => {
          this.adalSvc.userInfo.authenticated = true;
          sessionStorage.setItem(AppComponent.BasicAuthDataSessionStorageKey, basicAuthData);
          this.facilityService.initialize();
          this.router.navigate(['/']);
        },
          (error) => {
            this.busyService.idle();
            sessionStorage.removeItem(AppComponent.BasicAuthDataSessionStorageKey);
            this.authenticating = false;
            this.invalidAuth = true;
          });
    } else {
      this.adalSvc.login();
    }
  }
}
