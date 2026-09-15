import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AreaDto {
  id: number;
  nombre: string;
}

@Injectable({ providedIn: 'root' })
export class AreaService {
  private readonly baseUrl = `${environment.apiUrl}/areas`;

  constructor(private http: HttpClient) {}

  getAll(): Observable<AreaDto[]> {
    return this.http.get<AreaDto[]>(this.baseUrl);
  }

  // Busca un Area por nombre exacto; el backend la crea si no existe
  // (sincronizacion perezosa desde la BD institucional, ver
  // AreasController.Resolve). Usado por completar-registro con el nombre
  // que trae el claim "area" del token.
  resolve(nombre: string): Observable<AreaDto> {
    return this.http.post<AreaDto>(`${this.baseUrl}/resolve`, { nombre });
  }
}