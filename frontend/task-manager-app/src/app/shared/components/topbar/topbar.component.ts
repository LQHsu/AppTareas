import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialog } from '@angular/material/dialog';
import { UserDto, UserService } from '../../../core/services/user.service';
import { AuthService } from '../../../core/services/auth.service';
import { NotificacionesDialogComponent } from '../notificaciones-dialog/notificaciones-dialog.component';

// Barra superior institucional (logo UAM + usuario/logout). Puramente de
// layout: la navegacion (Carpetas/Proyectos/Mis tareas/Admin) sigue
// viviendo en el sidenav de app-shell, esto solo agrega la franja de
// arriba con la marca y la sesion activa. Adaptado del navbar.css de
// otro proyecto institucional (cafeteria), pero sin duplicar login -aca
// el usuario siempre esta autenticado (Keycloak ya lo exige antes de
// llegar al shell)- ni el resto de sus funciones (ayuda, menu movil,
// etc.), que no aplican en este proyecto.
@Component({
  selector: 'app-topbar',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatButtonModule, MatMenuModule],
  templateUrl: './topbar.component.html',
  styleUrl: './topbar.component.scss',
})
export class TopbarComponent {
  constructor(
    public userService: UserService,
    private auth: AuthService,
    private dialog: MatDialog
  ) {}

  logout(): void {
    this.auth.logout();
  }

  // Abre el menu de canales de notificacion (correo/Chat, ver
  // NotificacionesDialogComponent). Guarda apenas se cierra con
  // "Guardar" (dialogRef.close manda el DTO); con la X o Cancelar
  // afterClosed() llega undefined y no se hace nada.
  abrirNotificaciones(user: UserDto): void {
    this.dialog
      .open(NotificacionesDialogComponent, {
        data: { notifyByEmail: user.notifyByEmail, notifyByChat: user.notifyByChat },
        width: '420px',
        maxWidth: '95vw',
        autoFocus: false,
      })
      .afterClosed()
      .subscribe((dto) => {
        if (!dto) return;
        this.userService.updateNotificationPreferences(dto).subscribe();
      });
  }

  // El rol mas alto que tenga, no todos los que tenga (un
  // superadmin tambien puede ser coordinador, pero solo se muestra el de
  // mayor jerarquia). El resto simplemente no tiene ninguno de los dos.
  rolLabel(user: UserDto): string {
    if (user.isSuperAdmin) return 'Superadministrador';
    if (user.isCoordinador) return 'Coordinador';
    return 'Usuario';
  }
}
