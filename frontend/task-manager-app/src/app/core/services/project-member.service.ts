import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface ProjectMemberDto {
  userId: string;
  fullName: string;
  email: string;
  joinedAt: string;
  isOwner: boolean;
}

@Injectable({ providedIn: 'root' })
export class ProjectMemberService {
  private baseUrl(projectId: string): string {
    return `${environment.apiUrl}/projects/${projectId}/members`;
  }

  constructor(private http: HttpClient) {}

  getAll(projectId: string): Observable<ProjectMemberDto[]> {
    return this.http.get<ProjectMemberDto[]>(this.baseUrl(projectId));
  }

  add(projectId: string, userId: string): Observable<ProjectMemberDto> {
    return this.http.post<ProjectMemberDto>(this.baseUrl(projectId), { userId });
  }

  remove(projectId: string, userId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl(projectId)}/${userId}`);
  }
}