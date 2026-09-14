import { Component } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../../core/services/auth.service';

// A donde redirige banInterceptor cuando el backend responde 403 con
// { error: "account_banned" } (ver BanCheckMiddleware). Sin guard: no
// muestra nada sensible, y exigir sesion aca podria generar un loop si
// algo mas sale mal durante el chequeo de baneo.
@Component({
  selector: 'app-cuenta-bloqueada',
  standalone: true,
  imports: [MatCardModule, MatButtonModule, MatIconModule],
  templateUrl: './cuenta-bloqueada.component.html',
  styleUrl: './cuenta-bloqueada.component.scss',
})
export class CuentaBloqueadaComponent {
  constructor(private auth: AuthService) {}

  onLogout(): void {
    this.auth.logout();
  }
}
