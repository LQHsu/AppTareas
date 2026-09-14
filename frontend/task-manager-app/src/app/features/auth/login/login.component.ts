import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../../core/services/auth.service';
import { UserService } from '../../../core/services/user.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [MatCardModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent implements OnInit {
  checkingSession = false;

  constructor(
    public auth: AuthService,
    private userService: UserService,
    private router: Router
  ) {}

  ngOnInit(): void {
    if (this.auth.isAuthenticated()) {
      this.resolveUserAndRedirect();
    }
  }

  onLogin(): void {
    this.auth.login();
  }

  private resolveUserAndRedirect(): void {
    this.checkingSession = true;

    this.userService.getMe().subscribe((user) => {
      this.checkingSession = false;

      if (user) {
        this.router.navigate(['/proyectos']);
      } else {
        this.router.navigate(['/completar-registro']);
      }
    });
  }
}