import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface TaskAttachmentDto {
  id: string;
  taskId: string;
  fileName: string;
  contentType: string;
  fileSizeBytes: number;
  uploadedById: string;
  uploadedByFullName: string;
  uploadedAt: string;
}

// Adjunto visto desde el proyecto: agrega el titulo de la tarea a la
// que pertenece. Lo devuelve GET /api/projects/{id}/attachments.
export interface ProjectAttachmentDto extends TaskAttachmentDto {
  taskTitle: string;
}

// Helpers compartidos por las dos vistas que muestran adjuntos
// (tarea-card dentro del modal, y la pestaña de archivos del proyecto).

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

// El blob ya viene descargado con el header Authorization puesto (ver
// download()); esto solo lo entrega al navegador como archivo.
export function saveBlobAsFile(blob: Blob, fileName: string): void {
  const url = window.URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  window.URL.revokeObjectURL(url);
}

@Injectable({ providedIn: 'root' })
export class TaskAttachmentService {
  private baseUrl(taskId: string): string {
    return `${environment.apiUrl}/tasks/${taskId}/attachments`;
  }

  constructor(private http: HttpClient) {}

  getAll(taskId: string): Observable<TaskAttachmentDto[]> {
    return this.http.get<TaskAttachmentDto[]>(this.baseUrl(taskId));
  }

  // Todos los adjuntos de todas las tareas de un proyecto. Vive en este
  // servicio y no en ProjectService porque la descarga reusa download()
  // de aca (el DTO trae taskId justamente para eso).
  getByProject(projectId: string): Observable<ProjectAttachmentDto[]> {
    return this.http.get<ProjectAttachmentDto[]>(
      `${environment.apiUrl}/projects/${projectId}/attachments`
    );
  }

  upload(taskId: string, file: File): Observable<TaskAttachmentDto> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<TaskAttachmentDto>(this.baseUrl(taskId), formData);
  }

  // Se pide como blob (no un <a href> plano) porque la descarga requiere
  // el header Authorization: una navegacion directa del navegador no lo
  // manda, y el backend exige [Authorize].
  download(taskId: string, attachmentId: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl(taskId)}/${attachmentId}/download`, {
      responseType: 'blob',
    });
  }

  delete(taskId: string, attachmentId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(taskId)}/${attachmentId}`);
  }
}
