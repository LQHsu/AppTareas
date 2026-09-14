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
  createdAt: string;
  // Fecha del cambio de estado mas reciente (o createdAt si nunca cambio).
  lastStatusChangeAt: string;
  // Solo un nivel: el Subtasks de una subtarea siempre viene vacio.
  subtasks: TaskItemDto[];
}

export interface CreateTaskDto {
  title: string;
  description: string | null;
  projectId: string | null;
  assignedToId: string | null;
  parentTaskId: string | null;
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

  // description viene como HTML (lo produce app-editor-texto).
  updateDetails(taskId: string, title: string, description: string | null): Observable<TaskItemDto> {
    return this.http.patch<TaskItemDto>(`${this.baseUrl}/${taskId}/details`, { title, description });
  }
}