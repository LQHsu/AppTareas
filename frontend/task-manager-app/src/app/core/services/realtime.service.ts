import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';
import { TaskItemDto, TaskDeletedDto } from './task.service';
import { CommentDeletedDto, TaskCommentDto } from './task-comment.service';

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

  // Se emite cuando alguien borra (logicamente) una tarea - quien tenga
  // la lista abierta debe QUITAR esa fila, no esperar que llegue
  // actualizada por taskChanged$ (ver TasksController.Delete).
  readonly taskDeleted$ = new Subject<TaskDeletedDto>();

  // Comentarios en vivo (ver TaskCommentsController.NotificarComentario):
  // mismos grupos que taskChanged$ (proyecto del creador/asignado), asi
  // que llegan a cualquiera con esa tarea a la vista, no solo a quien
  // escribio el comentario.
  readonly commentAdded$ = new Subject<TaskCommentDto>();
  readonly commentDeleted$ = new Subject<CommentDeletedDto>();

  // SignalR no guarda mensajes: todo lo que paso mientras la conexion
  // estuvo caida se perdio. Se emite cada vez que se RECUPERA la conexion
  // (reconexion automatica, reintento tras caida, o al volver a la
  // pestana) para que quien tenga datos en pantalla los vuelva a pedir.
  readonly reconnected$ = new Subject<void>();

  // Proyectos a los que la pantalla pidio unirse. Al reconectar el
  // servidor asigna otro ConnectionId y pierde los grupos (solo re-une el
  // grupo personal), asi que hay que volver a unirse a mano.
  private joinedProjects = new Set<string>();
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private visibilityHooked = false;

  // Reintenta para siempre con tope de 30 s (el default de SignalR se
  // rinde tras 4 intentos, ~40 s, y deja la conexion muerta).
  private static retryDelayMs(previousRetryCount: number): number {
    return Math.min(1000 * 2 ** previousRetryCount, 30000);
  }

  // Idempotente: si ya esta conectado o conectandose, no hace nada.
  // Se llama una vez desde AppShellComponent (cubre cualquier pantalla
  // autenticada), no desde cada componente que necesita los eventos.
  async start(): Promise<void> {
    if (this.connection) return;

    // Al volver a la pestana (el navegador puede congelarla y matar el
    // socket sin avisar) se verifica que siga viva.
    if (!this.visibilityHooked) {
      this.visibilityHooked = true;
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState !== 'visible') return;
        if (!this.connection) {
          void this.start();
        } else if (this.connection.state === signalR.HubConnectionState.Connected) {
          // Pudo perderse algo mientras estaba en segundo plano.
          this.reconnected$.next();
        }
      });
    }

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
        accessTokenFactory: () => this.auth.getToken(),
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (ctx) => RealtimeService.retryDelayMs(ctx.previousRetryCount),
      })
      .build();

    this.connection.on('TaskChanged', (task: TaskItemDto) => {
      this.taskChanged$.next(task);
    });

    this.connection.on('TaskDeleted', (payload: TaskDeletedDto) => {
      this.taskDeleted$.next(payload);
    });

    this.connection.on('CommentAdded', (comment: TaskCommentDto) => {
      this.commentAdded$.next(comment);
    });

    this.connection.on('CommentDeleted', (payload: CommentDeletedDto) => {
      this.commentDeleted$.next(payload);
    });

    this.connection.onreconnected(() => void this.onRecovered());

    // Si se agotara el reintento automatico (o el servidor cierra la
    // conexion), se arranca de cero en vez de quedar sin tiempo real.
    this.connection.onclose(() => {
      this.connection = null;
      this.scheduleRestart();
    });

    try {
      await this.connection.start();
      // Solo notifica si antes hubo fallos: en el primer arranque la
      // pantalla recien cargo sus datos por HTTP.
      await this.onRecovered(this.retryCount > 0);
    } catch {
      // Si el hub no esta disponible (ej. backend viejo sin este
      // endpoint, o red caida), la app sigue funcionando igual que
      // antes de este feature: todo lo que dependia de HTTP normal no
      // se ve afectado, solo no hay actualizaciones en vivo. No hace
      // falta mostrar un error al usuario por esto. Se reintenta en
      // segundo plano.
      this.connection = null;
      this.scheduleRestart();
    }
  }

  private retryCount = 0;

  private scheduleRestart(): void {
    if (this.retryTimer) return;
    const delay = RealtimeService.retryDelayMs(this.retryCount++);
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      void this.start();
    }, delay);
  }

  // Conexion (re)establecida: re-une los grupos de proyecto y avisa para
  // que las pantallas recarguen lo que se pudo perder.
  private async onRecovered(notify = true): Promise<void> {
    this.retryCount = 0;
    for (const id of this.joinedProjects) {
      try {
        await this.connection?.invoke('JoinProject', id);
      } catch {
        // Se reintenta en la proxima recuperacion.
      }
    }
    // En el primer start() no hay nada que recargar: la pantalla recien
    // cargo sus datos por HTTP.
    if (notify) this.reconnected$.next();
  }

  // Se llama cuando proyecto-detalle abre/cierra: solo esa pantalla
  // necesita los cambios de TODAS las tareas del proyecto (no solo las
  // propias). Si la conexion todavia no termino de conectar, no hace
  // nada — no hay cola de reintento porque proyecto-detalle vuelve a
  // llamar joinProject cada vez que se monta.
  async joinProject(projectId: string): Promise<void> {
    // Se recuerda aunque no haya conexion todavia: onRecovered lo une
    // en cuanto conecte.
    this.joinedProjects.add(projectId);
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('JoinProject', projectId);
    }
  }

  async leaveProject(projectId: string): Promise<void> {
    this.joinedProjects.delete(projectId);
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('LeaveProject', projectId);
    }
  }
}
