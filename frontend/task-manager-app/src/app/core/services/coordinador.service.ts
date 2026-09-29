import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AltaCoordinadorDto {
  numeroEconomico: string;
  areaId: number;
  areaNombre: string;
  createdById: string;
  createdByNombre: string;
  createdAt: string;
  activatedAt: string | null;
  // Foto de correo/area institucional tomada al dar la alta (null si
  // info_usuarios_unidad no tenia dato en ese momento). No se actualiza
  // sola despues.
  correoInstitucional: string | null;
  areaInstitucional: string | null;
  // Nombre completo (CUSXACDI) tomado al dar la alta - null si CUSXACDI
  // no tenia dato en ese momento. No se actualiza solo despues.
  nombreCompleto: string | null;
  // Mientras esta pendiente: la oficina pre-elegida para cuando active
  // su cuenta. Una vez activada: la oficina VIGENTE (la carpeta que de
  // verdad tiene compartida en este momento, no una foto congelada) -
  // ver CoordinadorController.ToDtoAsync. Null si no tiene ninguna.
  oficinaId: string | null;
  oficinaNombre: string | null;
}

export interface AltaPreviewDto {
  numeroEconomico: string;
  nombreCompleto: string | null;
  correo: string | null;
  areaInstitucional: string | null;
  encontrado: boolean;
}

// Consume /api/coordinador: dar de alta numeros economicos autorizados
// a entrar a la app (ver AltaCoordinador en el backend). Especifico del
// proceso institucional de UAMX.
@Injectable({ providedIn: 'root' })
export class CoordinadorService {
  private readonly baseUrl = `${environment.apiUrl}/coordinador`;

  constructor(private http: HttpClient) {}

  getAltas(): Observable<AltaCoordinadorDto[]> {
    return this.http.get<AltaCoordinadorDto[]>(`${this.baseUrl}/altas`);
  }

  previewAlta(numeroEconomico: string): Observable<AltaPreviewDto> {
    return this.http.get<AltaPreviewDto>(`${this.baseUrl}/altas/preview/${encodeURIComponent(numeroEconomico)}`);
  }

  crearAlta(numeroEconomico: string, oficinaId: string | null = null): Observable<AltaCoordinadorDto> {
    return this.http.post<AltaCoordinadorDto>(`${this.baseUrl}/altas`, { numeroEconomico, oficinaId });
  }

  actualizarOficina(numeroEconomico: string, oficinaId: string | null): Observable<AltaCoordinadorDto> {
    return this.http.patch<AltaCoordinadorDto>(
      `${this.baseUrl}/altas/${encodeURIComponent(numeroEconomico)}/oficina`,
      { oficinaId }
    );
  }

  eliminarAlta(numeroEconomico: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/altas/${encodeURIComponent(numeroEconomico)}`);
  }

  // Vuelve a consultar CUSXACDI/info_usuarios_unidad y actualiza el
  // snapshot (nombre, correo, area institucional) - funciona aunque la
  // persona ya se haya activado, a diferencia del resto de esta
  // pantalla (esta tambien sirve para monitorear el estado
  // institucional de gente que ya entro a la app).
  refrescarDatos(numeroEconomico: string): Observable<AltaCoordinadorDto> {
    return this.http.post<AltaCoordinadorDto>(
      `${this.baseUrl}/altas/${encodeURIComponent(numeroEconomico)}/refrescar`,
      {}
    );
  }
}
