import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';
import { TaskItemDto } from './task.service';

// Conexion SignalR para recibir cambios de tareas en vivo. El hub
// (TaskHub en el backend) es de "solo escucha": nunca se le pide que
// mute datos, solo se conecta, se une a grupos, y reenvia el mismo
// TaskItemDto que ya usan los endpoints REST cuando algo cambia — no
// hay un modelo de datos paralelo que mantener sincronizado a mano.
//
// Se expone como Subject (no signal): un signal solo notifica su ultimo
// valor, y aca puede llegar mas de un cambio en el mismo tick (ej. una
// carpeta compartida reasigna varias tareas de golpe) — con un Subject
// cada emision llega a los suscriptores sin pisar a la anterior. Quien
// consume esto (proyecto-detalle, mis-tareas) sigue guardando SU propio
// estado en un signal, como ya hacian con las respuestas de TaskService.
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private auth = inject(AuthService);
  private connection: signalR.HubConnection | null = null;

  readonly taskChanged$ = new Subject<TaskItemDto>();

  // Idempotente: si ya esta conectado o conectandose, no hace nada.
  // Se llama una vez desde AppShellComponent (cubre cualquier pantalla
  // autenticada), no desde cada componente que necesita los eventos.
  async start(): Promise<void> {
    if (this.connection) return;

    // El host del hub es el mismo que el de la API, sin el sufijo
    // "/api" (environment.apiUrl lo trae para las llamadas REST).
    const hubUrl = `${environment.apiUrl.replace(/\/api\/?$/, '')}/hubs/tasks`;

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        // El token va por accessTokenFactory, no por header: el
        // handshake de websocket/SSE no puede llevar headers
        // personalizados, asi que el cliente de SignalR lo manda como
        // query string "access_token" (el backend lo acepta ahi, ver
        // Program.cs / MockAuthHandler, solo para rutas "/hubs").
        accessTokenFactory: () => this.auth.getToken() ?? '',
      })
      .withAutomaticReconnect()
      .build();

    this.connection.on('TaskChanged', (task: TaskItemDto) => {
      this.taskChanged$.next(task);
    });

    try {
      await this.connection.start();
    } catch {
      // Si el hub no esta disponible (ej. backend viejo sin este
      // endpoint, o red caida), la app sigue funcionando igual que
      // antes de este feature: todo lo que dependia de HTTP normal no
      // se ve afectado, solo no hay actualizaciones en vivo. No hace
      // falta mostrar un error al usuario por esto.
      this.connection = null;
    }
  }

  // Se llama cuando proyecto-detalle abre/cierra: solo esa pantalla
  // necesita los cambios de TODAS las tareas del proyecto (no solo las
  // propias). Si la conexion todavia no termino de conectar, no hace
  // nada — no hay cola de reintento porque proyecto-detalle vuelve a
  // llamar joinProject cada vez que se monta.
  async joinProject(projectId: string): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('JoinProject', projectId);
    }
  }

  async leaveProject(projectId: string): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('LeaveProject', projectId);
    }
  }
}
