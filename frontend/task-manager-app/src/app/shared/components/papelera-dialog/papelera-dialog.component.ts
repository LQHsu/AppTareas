import { Component, EventEmitter, Inject, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TaskItemDto, TaskService, TaskTrashItemDto, TASK_STATUS_LABELS } from '../../../core/services/task.service';

export interface PapeleraDialogData {
  // Ausente = papelera de tareas sueltas creadas por mi (ver
  // TasksController.GetTrash). Presente = papelera de ESE proyecto,
  // visible para cualquier miembro/dueno, no solo quien borro cada tarea.
  projectId?: string;
}

// Papelera de tareas borradas (ver TaskItem.IsDeleted): permite
// recuperar una tarea borrada por error con un click, sin tener que
// pedirle a nadie que la vuelva a crear a mano. No incluye "borrar para
// siempre" a proposito - el alcance pedido fue "recuperar", no limpieza
// definitiva; se puede agregar despues si hace falta.
@Component({
  selector: 'app-papelera-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule],
  templateUrl: './papelera-dialog.component.html',
  styleUrl: './papelera-dialog.component.scss',
})
export class PapeleraDialogComponent implements OnInit {
  items = signal<TaskTrashItemDto[]>([]);
  loading = signal(true);
  errorMessage = signal<string | null>(null);
  // Ids con una restauracion en curso, para deshabilitar su boton y no
  // permitir un doble click mientras se resuelve.
  restaurando = signal<Set<string>>(new Set());

  statusLabels = TASK_STATUS_LABELS;

  // Emite la tarea ya restaurada (con su DTO completo, tal cual la
  // devuelve el endpoint) para que quien abrio este dialog la vuelva a
  // meter en su lista principal sin tener que recargar todo.
  @Output() restored = new EventEmitter<TaskItemDto>();

  constructor(
    private taskService: TaskService,
    @Inject(MAT_DIALOG_DATA) public data: PapeleraDialogData
  ) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    this.taskService.getTrash(this.data.projectId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('No se pudo cargar la papelera.');
        this.loading.set(false);
      },
    });
  }

  restaurar(item: TaskTrashItemDto): void {
    this.restaurando.update((set) => new Set(set).add(item.id));

    this.taskService.restore(item.id).subscribe({
      next: (restored) => {
        this.items.update((list) => list.filter((i) => i.id !== item.id));
        this.restored.emit(restored);
      },
      error: () => {
        this.errorMessage.set(`No se pudo restaurar «${item.title}».`);
        this.restaurando.update((set) => {
          const next = new Set(set);
          next.delete(item.id);
          return next;
        });
      },
    });
  }
}
