import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface ProjectDto {
  id: string;
  name: string;
  description: string | null;
  areaId: number;
  areaNombre: string;
  ownerId: string;
  ownerFullName: string;
  isArchived: boolean;
  createdAt: string;
  totalTasks: number;
  completedTasks: number;
  // Carpeta en la que esta (null = sin carpeta). Un proyecto vive en
  // una sola carpeta.
  folderId: string | null;
  folderName: string | null;
  // Con quien esta compartida la carpeta del proyecto (null si no tiene
  // carpeta o la carpeta no esta compartida). Se usa para preseleccionar
  // el asignado por defecto al crear una tarea.
  folderSharedWithId: string | null;
  folderSharedWithFullName: string | null;
  // Color propio del proyecto (hex), independiente del color de su carpeta.
  color: string | null;
  // Color de la carpeta a la que pertenece (null si no tiene carpeta o
  // la carpeta no tiene color) - se usa como acento visual de pertenencia.
  folderColor: string | null;
}

export interface CreateProjectDto {
  name: string;
  description: string | null;
  // Si viene, el proyecto nace ya dentro de esa carpeta (debe ser
  // propia). El backend valida lo mismo que en moveToFolder.
  folderId?: string | null;
  color?: string | null;
}

@Injectable({ providedIn: 'root' })
export class ProjectService {
  private readonly baseUrl = `${environment.apiUrl}/projects`;

  constructor(private http: HttpClient) {}

  getMine(): Observable<ProjectDto[]> {
    return this.http.get<ProjectDto[]>(this.baseUrl);
  }

  getById(id: string): Observable<ProjectDto> {
    return this.http.get<ProjectDto>(`${this.baseUrl}/${id}`);
  }

  create(dto: CreateProjectDto): Observable<ProjectDto> {
    return this.http.post<ProjectDto>(this.baseUrl, dto);
  }

  // Solo nombre y descripcion; area y dueno no son editables.
  // Backend: solo el dueno del proyecto puede llamarlo.
  update(
    id: string,
    name: string,
    description: string | null,
    color: string | null = null
  ): Observable<ProjectDto> {
    return this.http.patch<ProjectDto>(`${this.baseUrl}/${id}`, { name, description, color });
  }

  // folderId null = sacarlo de la carpeta en la que este.
  moveToFolder(id: string, folderId: string | null): Observable<ProjectDto> {
    return this.http.patch<ProjectDto>(`${this.baseUrl}/${id}/folder`, { folderId });
  }
}