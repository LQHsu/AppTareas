import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface FolderDto {
  id: string;
  name: string;
  ownerId: string;
  ownerFullName: string;
  // Con quien esta compartida. Solo una persona: compartir reasigna las
  // tareas, y una tarea tiene un unico asignado.
  sharedWithId: string | null;
  sharedWithFullName: string | null;
  sharedAt: string | null;
  createdAt: string;
  projectCount: number;
  taskCount: number;
  // false cuando la carpeta me la compartieron: la veo pero no la administro.
  isOwner: boolean;
}

export interface ShareFolderResultDto {
  folder: FolderDto;
  reassignedTasks: number;
}

@Injectable({ providedIn: 'root' })
export class FolderService {
  private readonly baseUrl = `${environment.apiUrl}/folders`;

  constructor(private http: HttpClient) {}

  getMine(): Observable<FolderDto[]> {
    return this.http.get<FolderDto[]>(this.baseUrl);
  }

  create(name: string): Observable<FolderDto> {
    return this.http.post<FolderDto>(this.baseUrl, { name });
  }

  rename(id: string, name: string): Observable<FolderDto> {
    return this.http.patch<FolderDto>(`${this.baseUrl}/${id}`, { name });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  // userId con valor: comparte y REASIGNA todas las tareas de la carpeta
  // a esa persona (destructivo, pedir confirmacion antes).
  // userId null: deja de compartir, sin tocar las tareas.
  share(id: string, userId: string | null): Observable<ShareFolderResultDto> {
    return this.http.put<ShareFolderResultDto>(`${this.baseUrl}/${id}/share`, { userId });
  }
}
