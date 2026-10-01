import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTabsModule } from '@angular/material/tabs';
import {
  ScheduledNotificationDto,
  TASK_STATUS_LABELS,
  TaskItemDto,
  TaskItemStatus,
  TaskService,
} from '../../core/services/task.service';
import { UserService } from '../../core/services/user.service';
import { STATUS_COLORS, DonutSegment } from '../proyectos/proyecto-estadisticas/proyecto-estadisticas.component';

type FiltroAviso = 'todos' | 'pendientes' | 'enviados';

const inicioDelDia = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate());

// Pantalla de inicio (destino del login): accesos rapidos a Oficinas,
// Proyectos y Mis tareas, mas dos pestanas - la actividad de un dia
// (misma estetica que las estadisticas del proyecto) y los avisos de
// asignacion programados.
@Component({
  selector: 'app-home',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatTabsModule,
  ],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
})
export class HomeComponent implements OnInit {
  private taskService = inject(TaskService);
  private router = inject(Router);
  private userService = inject(UserService);

  readonly hoy = new Date();
  readonly statusLabels = TASK_STATUS_LABELS;

  readonly nombre = computed(() => this.userService.currentUser()?.fullName?.split(' ')[0] ?? '');
  readonly fechaHoy = this.hoy.toLocaleDateString('es-MX', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
  });

  readonly accesos = [
    { ruta: '/carpetas', icono: 'folder', titulo: 'Oficinas', detalle: 'Organiza tus proyectos por oficina' },
    { ruta: '/proyectos', icono: 'space_dashboard', titulo: 'Proyectos', detalle: 'Tableros y tareas del equipo' },
    { ruta: '/mis-tareas', icono: 'assignment_ind', titulo: 'Mis tareas', detalle: 'Lo que tienes asignado o creaste' },
  ];

  // ---------- Actividad del dia ----------
  dia = signal<Date>(inicioDelDia(new Date()));
  tareas = signal<TaskItemDto[]>([]);
  cargandoTareas = signal(true);
  errorTareas = signal<string | null>(null);

  total = computed(() => this.tareas().length);
  terminadas = computed(() => this.tareas().filter((t) => t.status === TaskItemStatus.Terminada).length);
  enCurso = computed(
    () =>
      this.tareas().filter(
        (t) => t.status !== TaskItemStatus.Terminada && t.status !== TaskItemStatus.Cancelada
      ).length
  );

  // Mismo calculo de dona que ProyectoEstadisticasComponent (radio 15.915
  // = circunferencia 100, el dasharray va directo en porcentajes).
  segments = computed<DonutSegment[]>(() => {
    const tareas = this.tareas();
    if (tareas.length === 0) return [];

    let acumulado = 0;
    return (Object.values(TaskItemStatus).filter((v) => typeof v === 'number') as TaskItemStatus[])
      .map((status) => {
        const count = tareas.filter((t) => t.status === status).length;
        const percent = (count / tareas.length) * 100;
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

  esHoy = computed(() => this.dia().getTime() === inicioDelDia(new Date()).getTime());

  // ---------- Avisos programados ----------
  avisos = signal<ScheduledNotificationDto[]>([]);
  cargandoAvisos = signal(true);
  errorAvisos = signal<string | null>(null);
  desde = signal<Date | null>(null);
  hasta = signal<Date | null>(null);
  filtroAviso = signal<FiltroAviso>('todos');

  avisosFiltrados = computed(() => {
    const f = this.filtroAviso();
    return this.avisos().filter((a) =>
      f === 'todos' ? true : f === 'pendientes' ? !a.sentAt : !!a.sentAt
    );
  });
  pendientes = computed(() => this.avisos().filter((a) => !a.sentAt).length);

  ngOnInit(): void {
    this.cargarTareas();
    this.cargarAvisos();
  }

  // ---------- Dia ----------
  cambiarDia(fecha: Date | null): void {
    if (!fecha) return;
    this.dia.set(inicioDelDia(fecha));
    this.cargarTareas();
  }

  irAHoy(): void {
    this.cambiarDia(new Date());
  }

  cargarTareas(): void {
    this.cargandoTareas.set(true);
    this.errorTareas.set(null);

    // Limites en hora LOCAL del dia elegido; el backend compara en UTC.
    const inicio = this.dia();
    const fin = new Date(inicio.getFullYear(), inicio.getMonth(), inicio.getDate() + 1);

    this.taskService.getModified(inicio, fin).subscribe({
      next: (tareas) => {
        this.tareas.set(tareas);
        this.cargandoTareas.set(false);
      },
      error: () => {
        this.errorTareas.set('No se pudo cargar la actividad del día.');
        this.cargandoTareas.set(false);
      },
    });
  }

  // ---------- Avisos ----------
  cambiarRango(): void {
    this.cargarAvisos();
  }

  limpiarRango(): void {
    this.desde.set(null);
    this.hasta.set(null);
    this.cargarAvisos();
  }

  cargarAvisos(): void {
    this.cargandoAvisos.set(true);
    this.errorAvisos.set(null);

    const desde = this.desde();
    const hasta = this.hasta();
    // "hasta" es inclusivo: el limite superior es el inicio del dia siguiente.
    const hastaExcl = hasta ? new Date(hasta.getFullYear(), hasta.getMonth(), hasta.getDate() + 1) : null;

    this.taskService.getScheduledNotifications(desde, hastaExcl).subscribe({
      next: (avisos) => {
        this.avisos.set(avisos);
        this.cargandoAvisos.set(false);
      },
      error: () => {
        this.errorAvisos.set('No se pudieron cargar los avisos programados.');
        this.cargandoAvisos.set(false);
      },
    });
  }

  // ---------- Navegacion al detalle ----------
  // Con proyecto: el detalle del proyecto abre la tarea (una subtarea abre
  // a su padre, ver ProyectoDetalleComponent). Suelta: Mis tareas.
  abrirTarea(taskId: string, projectId: string | null, parentTaskId: string | null = null): void {
    if (projectId) {
      this.router.navigate(['/proyectos', projectId], { queryParams: { tarea: parentTaskId ?? taskId } });
    } else {
      this.router.navigate(['/mis-tareas'], { queryParams: { tarea: taskId } });
    }
  }

  statusColor(status: TaskItemStatus): string {
    return STATUS_COLORS[status];
  }

  percentLabel(s: DonutSegment): number {
    return Math.round(s.percent);
  }

  // Fecha y hora cortas en es-MX, p. ej. "3 oct, 09:30".
  fechaHora(iso: string): string {
    return new Date(iso).toLocaleString('es-MX', {
      day: 'numeric',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    });
  }
}
