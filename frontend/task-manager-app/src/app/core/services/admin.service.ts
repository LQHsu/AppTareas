import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { UserDto } from './user.service';

// Vista global de un proyecto para el panel de super admin: agrega
// MemberCount respecto a ProjectDto, y no filtra por membresia (aqui se
// listan TODOS los proyectos, sin importar area/dueno/miembro).
export interface AdminProjectDto {
  id: string;
  name: string;
  description: string | null;
  areaId: number;
  areaNombre: string;
  ownerId: string;
  ownerFullName: string;
  isArchived: boolean;
  createdAt: string;
  memberCount: number;
  totalTasks: number;
  completedTasks: number;
}

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly baseUrl = `${environment.apiUrl}/admin`;

  constructor(private http: HttpClient) {}

  getAllUsers(): Observable<UserDto[]> {
    return this.http.get<UserDto[]>(`${this.baseUrl}/users`);
  }

  setSuperAdmin(userId: string, isSuperAdmin: boolean): Observable<UserDto> {
    return this.http.patch<UserDto>(`${this.baseUrl}/users/${userId}/super-admin`, { isSuperAdmin });
  }

  setBanned(userId: string, isBanned: boolean): Observable<UserDto> {
    return this.http.patch<UserDto>(`${this.baseUrl}/users/${userId}/ban`, { isBanned });
  }

  getAllProjects(): Observable<AdminProjectDto[]> {
    return this.http.get<AdminProjectDto[]>(`${this.baseUrl}/projects`);
  }
}
