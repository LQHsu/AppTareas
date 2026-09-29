import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

// Debe coincidir exactamente con TaskItemStatus del backend (mismo orden e ideas)
export enum TaskItemStatus {
  Creada = 0,
  Asignada = 1,
  Leida = 2,
  EnAtencion = 3,
  Atendida = 4,
  VolverARevisar = 5,
  Terminada = 6,
  Cancelada = 7,
}

export const TASK_STATUS_LABELS: Record<TaskItemStatus, string> = {
  [TaskItemStatus.Creada]: 'Creada',
  [TaskItemStatus.Asignada]: 'Asignada',
  [TaskItemStatus.Leida]: 'Leída',
  [TaskItemStatus.EnAtencion]: 'En atención',
  [TaskItemStatus.Atendida]: 'Atendida',
  [TaskItemStatus.VolverARevisar]: 'Volver a revisar',
  [TaskItemStatus.Terminada]: 'Terminada',
  [TaskItemStatus.Cancelada]: 'Cancelada',
};

export interface TaskItemDto {
  id: string;
  projectId: string | null;
  projectName: string | null;
  // Color del proyecto padre (hex), null si es tarea suelta o el
  // proyecto no tiene color. La tarea no tiene color propio.
  projectColor: string | null;
  // Oficina (carpeta) del proyecto padre - null si es tarea suelta, o
  // si el proyecto no esta en ninguna oficina.
  folderId: string | null;
  folderName: string | null;
  folderColor: string | null;
  parentTaskId: string | null;
  title: string;
  description: string | null;
  areaId: number;
  createdById: string;
  createdByFullName: string;
  assignedToId: string | null;
  assignedToFullName: string | null;
  status: TaskItemStatus;
  fechaAsignacion: string | null;
  fechaAtencion: string | null;
  fechaTerminacion: string | null;
  // Elegida a mano al crear/editar la tarea - puramente informativa, no
  // bloquea nada ni cambia el flujo de estados.
  fechaLimite: string | null;
  createdAt: string;
  // Fecha del cambio de estado mas reciente (o createdAt si nunca cambio).
  lastStatusChangeAt: string;
  // Solo para los iconos de "tiene adjuntos"/"tiene comentarios" en la
  // tabla: el backend los cuenta aparte, sin traer el contenido.
  commentCount: number;
  attachmentCount: number;
  // Solo un nivel: el Subtasks de una subtarea siempre viene vacio.
  subtasks: TaskItemDto[];
}

// Una fila del historial de cambios de estado. oldStatus null = la
// entrada de creacion. changedByFullName es quien hizo ESE cambio
// puntual - no se reescribe si la tarea despues cambia de asignado.
export interface TaskStatusHistoryDto {
  id: string;
  oldStatus: TaskItemStatus | null;
  newStatus: TaskItemStatus;
  changedById: string;
  changedByFullName: string;
  changedAt: string;
}

// fechaLimite es una fecha de calendario (sin hora), no un timestamp: el
// backend la guarda como medianoche UTC (mismo mecanismo que el resto de
// los DateTime, ver AppDbContext). Estas dos funciones son las unicas
// que deben tocar esa representacion - conviertiendo con Date/ISO a lo
// bruto en cualquier otro lado corre el riesgo de mostrar/guardar un dia
// distinto al elegido para quien esta en UTC-6 (Mexico).

// Del Date que entrega mat-datepicker (medianoche LOCAL del dia
// elegido) al string "yyyy-MM-dd" que espera el backend. NO se usa
// toISOString() a proposito: eso convierte a UTC y podria restar un dia
// para cualquiera al oeste de UTC.
export function fechaLimiteToString(date: Date | null): string | null {
  if (!date) return null;
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

// Del ISO (medianoche UTC) que regresa el backend a un Date para
// precargar mat-datepicker. Se leen los componentes en UTC (no local) y
// se arma un Date en medianoche LOCAL con esos mismos numeros - so el
// calendario muestra el mismo dia que se eligio, sin importar la zona
// horaria de quien lo ve.
export function fechaLimiteFromIso(iso: string | null): Date | null {
  if (!iso) return null;
  const parsed = new Date(iso);
  return new Date(parsed.getUTCFullYear(), parsed.getUTCMonth(), parsed.getUTCDate());
}

export interface CreateTaskDto {
  title: string;
  description: string | null;
  projectId: string | null;
  assignedToId: string | null;
  parentTaskId: string | null;
  fechaLimite?: string | null;
}

@Injectable({ providedIn: 'root' })
export class TaskService {
  private readonly baseUrl = `${environment.apiUrl}/tasks`;

  constructor(private http: HttpClient) {}

  getByProject(projectId: string): Observable<TaskItemDto[]> {
    return this.http.get<TaskItemDto[]>(`${this.baseUrl}?projectId=${projectId}`);
  }

  getMine(): Observable<TaskItemDto[]> {
    return this.http.get<TaskItemDto[]>(`${this.baseUrl}/mine`);
  }

  create(dto: CreateTaskDto): Observable<TaskItemDto> {
    return this.http.post<TaskItemDto>(this.baseUrl, dto);
  }

  updateStatus(taskId: string, status: TaskItemStatus): Observable<TaskItemDto> {
    return this.http.patch<TaskItemDto>(`${this.baseUrl}/${taskId}/status`, { status });
  }

  // Transicion automatica Asignada -> Leida: se llama al abrir el modal
  // de detalle (ver tarea-card / quien lo abra), no es una accion que
  // el usuario elija. Idempotente si ya paso de Asignada.
  markRead(taskId: string): Observable<TaskItemDto> {
    return this.http.patch<TaskItemDto>(`${this.baseUrl}/${taskId}/read`, {});
  }

  updateAssignee(taskId: string, assignedToId: string | null): Observable<TaskItemDto> {
    return this.http.patch<TaskItemDto>(`${this.baseUrl}/${taskId}/assign`, { assignedToId });
  }

  // description viene como HTML (lo produce app-editor-texto). fechaLimite
  // null = sin fecha limite (tambien sirve para quitarsela a una que ya tenia).
  updateDetails(
    taskId: string,
    title: string,
    description: string | null,
    fechaLimite: string | null = null
  ): Observable<TaskItemDto> {
    return this.http.patch<TaskItemDto>(`${this.baseUrl}/${taskId}/details`, {
      title,
      description,
      fechaLimite,
    });
  }

  // Mas reciente primero (ver TasksController.GetStatusHistory).
  getStatusHistory(taskId: string): Observable<TaskStatusHistoryDto[]> {
    return this.http.get<TaskStatusHistoryDto[]>(`${this.baseUrl}/${taskId}/status-history`);
  }
}