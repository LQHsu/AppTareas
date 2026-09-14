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
}