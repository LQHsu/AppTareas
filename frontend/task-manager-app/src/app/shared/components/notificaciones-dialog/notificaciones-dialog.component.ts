import { Component, Inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { UpdateNotificationPreferencesDto } from '../../../core/services/user.service';

export interface NotificacionesDialogData {
  notifyByEmail: boolean;
  notifyByChat: boolean;
}

// Menu de "activar/desactivar notificaciones" (ver TaskNotificationService
// en el backend, que es quien de verdad decide si manda o no cada canal
// segun estos dos flags). Los dos toggles son independientes: alguien
// puede querer solo correo, solo Chat, ambos o ninguno - no hay un
// interruptor maestro porque no aporta nada que los dos juntos no den
// ya, y hubiera sido una tercera fuente de verdad que sincronizar.
@Component({
  selector: 'app-notificaciones-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatSlideToggleModule],
  templateUrl: './notificaciones-dialog.component.html',
  styleUrl: './notificaciones-dialog.component.scss',
})
export class NotificacionesDialogComponent {
  notifyByEmail = signal(false);
  notifyByChat = signal(false);

  constructor(
    public dialogRef: MatDialogRef<NotificacionesDialogComponent, UpdateNotificationPreferencesDto>,
    @Inject(MAT_DIALOG_DATA) public data: NotificacionesDialogData
  ) {

    this.notifyByEmail.set(data.notifyByEmail)
    this.notifyByChat.set(data.notifyByChat )
  }

  guardar(): void {
    this.dialogRef.close({
      notifyByEmail: this.notifyByEmail(),
      notifyByChat: this.notifyByChat(),
    });
  }
}
