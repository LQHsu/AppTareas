import { Component, Inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { FolderDto } from '../../../core/services/folder.service';
import { UserDto } from '../../../core/services/user.service';

export interface CompartirCarpetaDialogData {
  folder: FolderDto;
  candidatos: UserDto[];
}

// Confirmacion para compartir una carpeta. Existe sobre todo por el
// aviso: compartir REASIGNA todas las tareas de la carpeta a esa
// persona, pisando a quien las tuviera, y no se puede deshacer porque
// el modelo no guarda historial de asignaciones.
@Component({
  selector: 'app-compartir-carpeta-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatSelectModule,
  ],
  templateUrl: './compartir-carpeta-dialog.component.html',
  styleUrl: './compartir-carpeta-dialog.component.scss',
})
export class CompartirCarpetaDialogComponent {
  seleccionado = signal<string | null>(null);

  constructor(
    public dialogRef: MatDialogRef<CompartirCarpetaDialogComponent, string>,
    @Inject(MAT_DIALOG_DATA) public data: CompartirCarpetaDialogData
  ) {}

  confirmar(): void {
    const userId = this.seleccionado();
    if (userId) this.dialogRef.close(userId);
  }
}
