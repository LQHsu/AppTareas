import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface TaskCommentDto {
  id: string;
  taskId: string;
  userId: string;
  userFullName: string;
  content: string;
  createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class TaskCommentService {
  private baseUrl(taskId: string): string {
    return `${environment.apiUrl}/tasks/${taskId}/comments`;
  }

  constructor(private http: HttpClient) {}

  getAll(taskId: string): Observable<TaskCommentDto[]> {
    return this.http.get<TaskCommentDto[]>(this.baseUrl(taskId));
  }

  create(taskId: string, content: string): Observable<TaskCommentDto> {
    return this.http.post<TaskCommentDto>(this.baseUrl(taskId), { content });
  }

  delete(taskId: string, commentId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(taskId)}/${commentId}`);
  }
}
