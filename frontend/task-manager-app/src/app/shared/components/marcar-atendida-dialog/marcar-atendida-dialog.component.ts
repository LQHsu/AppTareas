import { Component, Inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

export interface MarcarAtendidaDialogData {
  taskTitle: string;
}

// Se abre al marcar una tarea como "Atendida" (ver onStatusChange en
// proyecto-detalle/mis-tareas): invita a dejar un comentario explicando
// que se hizo, reusando TaskCommentService tal cual - el comentario
// queda como cualquier otro (visible/editable/borrable igual, y con el
// mismo broadcast en vivo por SignalR), esto solo prellena el momento en
// que se pide.
//
// El comentario es obligatorio (el boton "Marcar como atendida" queda
// deshabilitado hasta que se escriba algo, ver el .html) - quien asigna
// la tarea necesita saber que se hizo. undefined = canceló todo el
// cambio de estado.
@Component({
  selector: 'app-marcar-atendida-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './marcar-atendida-dialog.component.html',
  styleUrl: './marcar-atendida-dialog.component.scss',
})
export class MarcarAtendidaDialogComponent {
  comentario = signal('');

  constructor(
    public dialogRef: MatDialogRef<MarcarAtendidaDialogComponent, string>,
    @Inject(MAT_DIALOG_DATA) public data: MarcarAtendidaDialogData
  ) {}

  confirmar(): void {
    this.dialogRef.close(this.comentario().trim());
  }
}
