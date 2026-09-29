import { HttpClient } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { Observable, catchError, of, tap } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface UserDto {
  id: string;
  username: string;
  email: string;
  fullName: string;
  areaId: number;
  areaNombre: string;
  isSuperAdmin: boolean;
  isBanned: boolean;
  isCoordinador: boolean;
  notifyByEmail: boolean;
  notifyByChat: boolean;
}

export interface UpdateNotificationPreferencesDto {
  notifyByEmail: boolean;
  notifyByChat: boolean;
}

export interface CreateUserDto {
  id: string;
  username: string;
  email: string;
  fullName: string;
  areaId: number;
}

@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly baseUrl = `${environment.apiUrl}/users`;

  // Cache del usuario actual (incluye isSuperAdmin), poblada por
  // getMe(). La usan cosas como el link "Admin" del nav y el guard de
  // /admin para no tener que volver a pedirlo en cada lugar que lo
  // necesita. Se actualiza cada vez que se llama getMe() de nuevo.
  readonly currentUser = signal<UserDto | null>(null);

  constructor(private http: HttpClient) {}

  getMe(): Observable<UserDto | null> {
    return this.http.get<UserDto>(`${this.baseUrl}/me`).pipe(
      tap((user) => this.currentUser.set(user)),
      catchError(() => {
        this.currentUser.set(null);
        return of(null);
      })
    );
  }

  register(dto: CreateUserDto): Observable<UserDto> {
    return this.http.post<UserDto>(this.baseUrl, dto);
  }

  // Catalogo de usuarios de una misma area, usado para invitar
  // gente a un proyecto o asignar tareas sueltas.
  getByArea(areaId: number): Observable<UserDto[]> {
    return this.http.get<UserDto[]>(`${this.baseUrl}?areaId=${areaId}`);
  }

  // Actualiza currentUser con la respuesta para que el resto de la app
  // (ej. si se vuelve a abrir el dialogo) vea el valor recien guardado
  // sin tener que llamar getMe() de nuevo.
  updateNotificationPreferences(dto: UpdateNotificationPreferencesDto): Observable<UserDto> {
    return this.http.patch<UserDto>(`${this.baseUrl}/me/notifications`, dto).pipe(
      tap((user) => this.currentUser.set(user))
    );
  }
}