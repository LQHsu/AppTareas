import { Component, computed, input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TaskItemDto, TaskItemStatus, TASK_STATUS_LABELS } from '../../../core/services/task.service';
import { ProjectMemberDto } from '../../../core/services/project-member.service';

// Valor del filtro "por usuario": 'all' = todos, null = sin asignar,
// string = id del miembro.
type FiltroUsuario = 'all' | null | string;

// Colores del grafico: los mismos --estado-* que usan los badges de
// TareaCardComponent/TareasTablaComponent (definidos en styles.scss),
// referenciados via var() en vez de duplicar los hex aca. Antes este
// grafico tenia su propia paleta separada (azul/verde/magenta + colores
// sueltos) y un mismo estado se veia con un color en el badge y otro en
// la dona — con esto un estado significa el mismo color en toda la app.
const STATUS_COLORS: Record<TaskItemStatus, string> = {
  [TaskItemStatus.Creada]: 'var(--estado-creada)',
  [TaskItemStatus.Asignada]: 'var(--estado-asignada)',
  [TaskItemStatus.Leida]: 'var(--estado-leida)',
  [TaskItemStatus.EnAtencion]: 'var(--estado-en-atencion)',
  [TaskItemStatus.Atendida]: 'var(--estado-atendida)',
  [TaskItemStatus.VolverARevisar]: 'var(--estado-volver-a-revisar)',
  [TaskItemStatus.Terminada]: 'var(--estado-terminada)',
  [TaskItemStatus.Cancelada]: 'var(--estado-cancelada)',
};

export interface DonutSegment {
  status: TaskItemStatus;
  label: string;
  color: string;
  count: number;
  percent: number;
  dashArray: string;
  dashOffset: number;
}

// Estadisticas del proyecto: % de completado, totales y reparto de
// tareas por estado, todo filtrable por persona asignada.
//
// El grafico de dona es SVG inline en vez de una libreria de charts: es
// el unico grafico de la app y el calculo es trivial, no vale la pena
// meter una dependencia nueva al stack.
@Component({
  selector: 'app-proyecto-estadisticas',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatIconModule,
    MatFormFieldModule,
    MatSelectModule,
    MatProgressBarModule,
  ],
  templateUrl: './proyecto-estadisticas.component.html',
  styleUrl: './proyecto-estadisticas.component.scss',
})
export class ProyectoEstadisticasComponent {
  // Inputs como signals (no @Input clasico) para poder derivar todas las
  // metricas con computed() y que se recalculen solas cuando el padre
  // actualiza la lista de tareas.
  tasks = input.required<TaskItemDto[]>();
  members = input.required<ProjectMemberDto[]>();

  filtroUsuario = signal<FiltroUsuario>('all');

  // Las subtareas tambien cuentan: vienen anidadas dentro de cada tarea
  // top-level (ver TaskItemDto.subtasks), asi que hay que aplanarlas.
  private allTasks = computed(() => this.tasks().flatMap((t) => [t, ...t.subtasks]));

  filteredTasks = computed(() => {
    const filtro = this.filtroUsuario();
    if (filtro === 'all') return this.allTasks();
    return this.allTasks().filter((t) => t.assignedToId === filtro);
  });

  total = computed(() => this.filteredTasks().length);

  completadas = computed(
    () => this.filteredTasks().filter((t) => t.status === TaskItemStatus.Terminada).length
  );

  canceladas = computed(
    () => this.filteredTasks().filter((t) => t.status === TaskItemStatus.Cancelada).length
  );

  // Misma formula que usa el backend para el % de un proyecto:
  // Terminadas / (Total - Canceladas). Las canceladas no cuentan como
  // trabajo pendiente ni como completado.
  porcentaje = computed(() => {
    const divisor = this.total() - this.canceladas();
    if (divisor <= 0) return 0;
    return Math.round((this.completadas() / divisor) * 100);
  });

  // Segmentos del donut. El truco del radio 15.915 es que la
  // circunferencia queda en 100, asi el stroke-dasharray se puede
  // expresar directamente en porcentajes.
  segments = computed<DonutSegment[]>(() => {
    const tasks = this.filteredTasks();
    if (tasks.length === 0) return [];

    let acumulado = 0;

    return (Object.values(TaskItemStatus).filter((v) => typeof v === 'number') as TaskItemStatus[])
      .map((status) => {
        const count = tasks.filter((t) => t.status === status).length;
        const percent = (count / tasks.length) * 100;
        const segment: DonutSegment = {
          status,
          label: TASK_STATUS_LABELS[status],
          color: STATUS_COLORS[status],
          count,
          percent,
          dashArray: `${percent} ${100 - percent}`,
          dashOffset: -acumulado,
        };
        acumulado += percent;
        return segment;
      })
      .filter((s) => s.count > 0);
  });

  // Redondeado solo para mostrar en la leyenda (el del donut usa el
  // valor exacto para que los segmentos cierren bien).
  percentLabel(segment: DonutSegment): number {
    return Math.round(segment.percent);
  }
}
