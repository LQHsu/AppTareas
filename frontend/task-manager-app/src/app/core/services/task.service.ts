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
  Pausada = 8,
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
  [TaskItemStatus.Pausada]: 'Pausada',
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

// Fila de la papelera (ver TasksController.GetTrash) - DTO reducido,
// sin subtareas/conteos/historial, solo lo necesario para decidir
// restaurar o dejarla ahi.
export interface TaskTrashItemDto {
  id: string;
  projectId: string | null;
  projectName: string | null;
  parentTaskId: string | null;
  title: string;
  status: TaskItemStatus;
  createdByFullName: string;
  assignedToFullName: string | null;
  deletedAt: string;
  deletedByFullName: string;
}

// Payload del evento SignalR "TaskDeleted" (ver RealtimeService) -
// deliberadamente minimo, ver TaskDeletedDto en el backend.
export interface TaskDeletedDto {
  id: string;
  parentTaskId: string | null;
  projectId: string | null;
}

// Aviso de asignacion programado (ver ScheduledTaskNotification en el
// backend). sentAt nulo = pendiente; con valor = ya se envio.
export interface ScheduledNotificationDto {
  id: string;
  taskId: string;
  taskTitle: string;
  projectId: string | null;
  projectName: string | null;
  recipientFullName: string;
  sendAt: string;
  sentAt: string | null;
}

export interface CreateTaskDto {
  title: string;
  description: string | null;
  projectId: string | null;
  assignedToId: string | null;
  parentTaskId: string | null;
  fechaLimite?: string | null;
  // ISO en UTC. Si esta en el futuro, el aviso de asignacion se encola y
  // sale a esa hora; nulo = aviso inmediato (ver notificarEnToIso).
  notificarEn?: string | null;
}

// Del valor de un <input type="datetime-local"> ("yyyy-MM-ddTHH:mm", hora
// LOCAL de quien lo elige, sin zona) al ISO en UTC que espera el backend
// (NotificarEn). new Date(string) sin zona lo interpreta como local, y
// toISOString() lo pasa a UTC - asi "9:00" en Mexico llega como 15:00Z.
export function notificarEnToIso(local: string | null | undefined): string | null {
  if (!local) return null;
  const fecha = new Date(local);
  return isNaN(fecha.getTime()) ? null : fecha.toISOString();
}

@Injectable({ providedIn: 'root' })
export class TaskService {
  private readonly baseUrl = `${environment.apiUrl}/tasks`;

  constructor(private http: HttpClient) {}

  getByProject(projectId: string): Observable<TaskItemDto[]> {
    return this.http.get<TaskItemDto[]>(`${this.baseUrl}?projectId=${projectId}`);
  }

  // Resumen de Inicio: tareas visibles para mi modificadas en [from, to).
  getModified(from: Date, to: Date): Observable<TaskItemDto[]> {
    return this.http.get<TaskItemDto[]>(`${this.baseUrl}/modified`, {
      params: { from: from.toISOString(), to: to.toISOString() },
    });
  }

  // Avisos de asignacion programados por mi (pendientes y enviados).
  // from/to opcionales: filtran por la hora programada.
  getScheduledNotifications(from?: Date | null, to?: Date | null): Observable<ScheduledNotificationDto[]> {
    const params: Record<string, string> = {};
    if (from) params['from'] = from.toISOString();
    if (to) params['to'] = to.toISOString();
    return this.http.get<ScheduledNotificationDto[]>(`${this.baseUrl}/scheduled-notifications`, { params });
  }

  getMine(): Observable<TaskItemDto[]> {
    return this.http.get<TaskItemDto[]>(`${this.baseUrl}/mine`);
  }

  create(dto: CreateTaskDto): Observable<TaskItemDto> {
    return this.http.post<TaskItemDto>(this.baseUrl, dto);
  }

  // comment: nota opcional, solo se usa al pasar a Atendida - el backend
  // la guarda como comentario y la incluye en el aviso de "atendida".
  updateStatus(taskId: string, status: TaskItemStatus, comment: string | null = null): Observable<TaskItemDto> {
    return this.http.patch<TaskItemDto>(`${this.baseUrl}/${taskId}/status`, { status, comment });
  }

  // Transicion automatica Asignada -> Leida: se llama al abrir el modal
  // de detalle (ver tarea-card / quien lo abra), no es una accion que
  // el usuario elija. Idempotente si ya paso de Asignada.
  markRead(taskId: string): Observable<TaskItemDto> {
    return this.http.patch<TaskItemDto>(`${this.baseUrl}/${taskId}/read`, {});
  }

  updateAssignee(
    taskId: string,
    assignedToId: string | null,
    notificarEn: string | null = null
  ): Observable<TaskItemDto> {
    return this.http.patch<TaskItemDto>(`${this.baseUrl}/${taskId}/assign`, {
      assignedToId,
      notificarEn,
    });
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

  // Borrado logico (ver TaskItem.IsDeleted): la tarea (y sus subtareas)
  // se van a la papelera, no se pierden. El aviso en vivo llega como
  // "TaskDeleted" por RealtimeService, no como respuesta de este metodo.
  delete(taskId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${taskId}`);
  }

  // projectId ausente: papelera de tareas sueltas creadas por mi.
  getTrash(projectId?: string): Observable<TaskTrashItemDto[]> {
    const query = projectId ? `?projectId=${projectId}` : '';
    return this.http.get<TaskTrashItemDto[]>(`${this.baseUrl}/trash${query}`);
  }

  restore(taskId: string): Observable<TaskItemDto> {
    return this.http.post<TaskItemDto>(`${this.baseUrl}/${taskId}/restore`, {});
  }
}