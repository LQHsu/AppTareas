import { Component, EventEmitter, Inject, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TaskItemDto, TaskItemStatus } from '../../../core/services/task.service';
import { ProjectMemberDto } from '../../../core/services/project-member.service';
import { TareaCardComponent } from '../tarea-card/tarea-card.component';

export interface TareaDetalleDialogData {
  task: TaskItemDto;
  members: ProjectMemberDto[] | null;
  showProject: boolean;
  // El dueno del proyecto tiene permisos de "creador" sobre el estado
  // de cualquier tarea del proyecto (ver TareaCardComponent). Opcional:
  // mis-tareas no tiene un unico proyecto/dueno de referencia y lo omite.
  isProjectOwner?: boolean;
}

// Modal de detalle: reutiliza TareaCardComponent tal cual (misma logica
// de edicion de estado/asignado/subtareas/adjuntos), solo que aqui vive
// sola en vez de apilada en una lista. Quien abre el dialog (via
// MatDialog.open) es responsable de escuchar estos outputs y de volver
// a llamar a `task.set(...)` con la version actualizada despues de cada
// cambio -el dialog no habla con el backend directamente, reusa los
// handlers que ya existen en proyecto-detalle/mis-tareas.
@Component({
  selector: 'app-tarea-detalle-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatIconModule, TareaCardComponent],
  templateUrl: './tarea-detalle-dialog.component.html',
  styleUrl: './tarea-detalle-dialog.component.scss',
})
export class TareaDetalleDialogComponent {
  // Se inicializa en el constructor, no como field initializer: depende
  // de `this.data`, que se inyecta por constructor (mismo gotcha ya
  // documentado en Estado-Proyecto.md).
  task: ReturnType<typeof signal<TaskItemDto>>;

  @Output() assigneeChange = new EventEmitter<string | null>();
  @Output() statusChange = new EventEmitter<TaskItemStatus>();
  @Output() subtaskAssigneeChange = new EventEmitter<{
    subtask: TaskItemDto;
    assignedToId: string | null;
  }>();
  @Output() subtaskStatusChange = new EventEmitter<{ subtask: TaskItemDto; status: TaskItemStatus }>();
  @Output() subtaskCreate = new EventEmitter<{ title: string; assignedToId: string | null }>();
  @Output() detailsChange = new EventEmitter<{ title: string; description: string | null }>();
  @Output() subtaskDetailsChange = new EventEmitter<{
    subtask: TaskItemDto;
    title: string;
    description: string | null;
  }>();

  constructor(@Inject(MAT_DIALOG_DATA) public data: TareaDetalleDialogData) {
    this.task = signal<TaskItemDto>(this.data.task);
  }
}
