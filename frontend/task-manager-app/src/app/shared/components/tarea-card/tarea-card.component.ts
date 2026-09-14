import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import {
  TaskItemDto,
  TaskItemStatus,
  TASK_STATUS_LABELS,
} from '../../../core/services/task.service';
import { ProjectMemberDto } from '../../../core/services/project-member.service';
import {
  TaskAttachmentDto,
  TaskAttachmentService,
  formatFileSize,
  saveBlobAsFile,
} from '../../../core/services/task-attachment.service';
import { TaskCommentDto, TaskCommentService } from '../../../core/services/task-comment.service';
import { EditorTextoComponent } from '../editor-texto/editor-texto.component';
import { UserService } from '../../../core/services/user.service';

// Tarjeta de tarea reutilizable: la usan tanto proyecto-detalle (donde
// se conoce la lista de miembros del proyecto y se puede reasignar)
// como mis-tareas (donde las tareas vienen de varios proyectos y el
// campo "Asignada a" se muestra solo de lectura).
//
// Tambien renderiza sus subtareas (un solo nivel: la propia instancia
// se vuelve a usar para cada subtarea con isSubtask=true, que oculta la
// seccion de subtareas y el boton de "agregar subtarea" para no poder
// anidar mas de un nivel). Los cambios de estado/asignado y la creacion
// de una subtarea se emiten hacia arriba para que el componente padre
// (quien sabe hablar con el backend) los resuelva.
//
// Los adjuntos y los comentarios son la excepcion a ese patron: en vez
// de emitir eventos, esta tarjeta inyecta sus servicios directamente y
// maneja su propia lista (carga perezosa al expandir la seccion). Se
// hizo asi para que proyecto-detalle y mis-tareas no tengan que cablear
// nada extra: "simplemente funcionan" en cualquier lugar donde se use
// esta tarjeta, a diferencia de estado/asignado/subtareas que si
// dependen de contexto que solo el componente padre conoce (lista de
// miembros, projectId, etc.).
@Component({
  selector: 'app-tarea-card',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatIconModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    EditorTextoComponent,
  ],
  templateUrl: './tarea-card.component.html',
  styleUrl: './tarea-card.component.scss',
})
export class TareaCardComponent {
  @Input({ required: true }) task!: TaskItemDto;

  // Si se pasa, el campo "Asignada a" se vuelve un select editable con
  // estos candidatos. Si se omite, se muestra solo de lectura.
  @Input() members: ProjectMemberDto[] | null = null;

  // Muestra el proyecto de origen de la tarea (o "Tarea suelta").
  // Util en vistas que mezclan tareas de varios proyectos.
  @Input() showProject = false;

  // true cuando esta instancia representa una subtarea renderizada
  // dentro de otra tarjeta: oculta su propia seccion de subtareas.
  @Input() isSubtask = false;

  // El dueno del proyecto cuenta como "creador" para efectos de permisos
  // de estado, aunque no haya creado esta tarea en particular (ver
  // puedeCambiarA). false por default: en mis-tareas no hay un unico
  // proyecto/dueno de referencia, asi que ahi nunca aplica.
  @Input() isProjectOwner = false;

  @Output() assigneeChange = new EventEmitter<string | null>();
  @Output() statusChange = new EventEmitter<TaskItemStatus>();
  @Output() subtaskAssigneeChange = new EventEmitter<{
    subtask: TaskItemDto;
    assignedToId: string | null;
  }>();
  @Output() subtaskStatusChange = new EventEmitter<{ subtask: TaskItemDto; status: TaskItemStatus }>();
  @Output() subtaskCreate = new EventEmitter<{ title: string; assignedToId: string | null }>();
  @Output() detailsChange = new EventEmitter<{ title: string; description: string | null }>();
  @Output() subtaskDetailsChange = new EventEmitter<{
    subtask: TaskItemDto;
    title: string;
    description: string | null;
  }>();

  statusOptions = Object.values(TaskItemStatus).filter(
    (v) => typeof v === 'number'
  ) as TaskItemStatus[];
  statusLabels = TASK_STATUS_LABELS;

  private userService = inject(UserService);

  // --- Permisos de transicion de estado ---
  // Reglas de negocio (mismas que valida el backend en
  // TasksController.UpdateStatus — esto es solo para no mostrar opciones
  // que de todos modos el servidor va a rechazar):
  // - Creada/Asignada/Leida son automaticas (creacion, asignacion,
  //   apertura del modal). Solo quien creo la tarea (o el dueno del
  //   proyecto) las puede forzar a mano.
  // - En atencion/Atendida: las mueve quien tiene la tarea asignada, o
  //   quien la creo.
  // - Volver a revisar/Terminada/Cancelada: decision de quien creo la
  //   tarea (o el dueno del proyecto).
  private get esCreador(): boolean {
    const uid = this.userService.currentUser()?.id;
    return !!uid && (this.task.createdById === uid || this.isProjectOwner);
  }

  private get esAsignado(): boolean {
    const uid = this.userService.currentUser()?.id;
    return !!uid && this.task.assignedToId === uid;
  }

  get puedeEditarEstado(): boolean {
    return this.esCreador || this.esAsignado;
  }

  puedeCambiarA(status: TaskItemStatus): boolean {
    if (this.esCreador) return true;
    if (this.esAsignado) {
      return status === TaskItemStatus.EnAtencion || status === TaskItemStatus.Atendida;
    }
    return false;
  }

  // Tono visual del badge de estado: un color por estado (mismo esquema
  // que TareasTablaComponent.badgeEstado — los colores viven en
  // styles.scss como --estado-*, aca solo se arma el sufijo de clase).
  private static readonly SUFIJOS_ESTADO: Record<TaskItemStatus, string> = {
    [TaskItemStatus.Creada]: 'creada',
    [TaskItemStatus.Asignada]: 'asignada',
    [TaskItemStatus.Leida]: 'leida',
    [TaskItemStatus.EnAtencion]: 'en-atencion',
    [TaskItemStatus.Atendida]: 'atendida',
    [TaskItemStatus.VolverARevisar]: 'volver-a-revisar',
    [TaskItemStatus.Terminada]: 'terminada',
    [TaskItemStatus.Cancelada]: 'cancelada',
  };

  statusTone(status: TaskItemStatus): string {
    return TareaCardComponent.SUFIJOS_ESTADO[status];
  }

  showAddSubtask = false;
  newSubtaskTitle = '';
  newSubtaskAssignedToId: string | null = null;

  // --- Edicion de titulo y descripcion ---
  // Se editan juntos (un solo boton "Editar" y un solo guardado)
  // porque el backend los recibe en el mismo PATCH /details.

  editando = signal(false);
  edicionTitulo = '';
  edicionDescripcion: string | null = null;

  entrarEdicion(): void {
    this.edicionTitulo = this.task.title;
    this.edicionDescripcion = this.task.description;
    this.editando.set(true);
  }

  cancelarEdicion(): void {
    this.editando.set(false);
  }

  guardarEdicion(): void {
    const titulo = this.edicionTitulo.trim();
    if (!titulo) return;

    this.detailsChange.emit({ title: titulo, description: this.edicionDescripcion });
    this.editando.set(false);
  }

  constructor(
    private attachmentService: TaskAttachmentService,
    private commentService: TaskCommentService
  ) {}

  toggleAddSubtask(): void {
    this.showAddSubtask = !this.showAddSubtask;
  }

  onCreateSubtask(): void {
    if (!this.newSubtaskTitle.trim()) return;

    this.subtaskCreate.emit({
      title: this.newSubtaskTitle.trim(),
      assignedToId: this.newSubtaskAssignedToId,
    });

    this.newSubtaskTitle = '';
    this.newSubtaskAssignedToId = null;
    this.showAddSubtask = false;
  }

  // --- Adjuntos ---

  showAttachments = signal(false);
  loadingAttachments = signal(false);
  uploadingAttachment = signal(false);
  attachments = signal<TaskAttachmentDto[]>([]);
  attachmentError = signal<string | null>(null);

  toggleAttachments(): void {
    const opening = !this.showAttachments();
    this.showAttachments.set(opening);

    if (opening && this.attachments().length === 0) {
      this.loadAttachments();
    }
  }

  private loadAttachments(): void {
    this.loadingAttachments.set(true);
    this.attachmentService.getAll(this.task.id).subscribe({
      next: (list) => {
        this.attachments.set(list);
        this.loadingAttachments.set(false);
      },
      error: () => {
        this.loadingAttachments.set(false);
        this.attachmentError.set('No se pudieron cargar los adjuntos.');
      },
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // permite volver a elegir el mismo archivo despues

    if (!file) return;

    this.attachmentError.set(null);
    this.uploadingAttachment.set(true);

    this.attachmentService.upload(this.task.id, file).subscribe({
      next: (uploaded) => {
        this.attachments.update((list) => [uploaded, ...list]);
        this.uploadingAttachment.set(false);
      },
      error: () => {
        this.uploadingAttachment.set(false);
        this.attachmentError.set('No se pudo subir el archivo (revisa tamaño y tipo permitido).');
      },
    });
  }

  onDownloadAttachment(attachment: TaskAttachmentDto): void {
    this.attachmentService.download(this.task.id, attachment.id).subscribe({
      next: (blob) => saveBlobAsFile(blob, attachment.fileName),
      error: () => {
        this.attachmentError.set('No se pudo descargar el archivo.');
      },
    });
  }

  onDeleteAttachment(attachment: TaskAttachmentDto): void {
    this.attachmentService.delete(this.task.id, attachment.id).subscribe({
      next: () => {
        this.attachments.update((list) => list.filter((a) => a.id !== attachment.id));
      },
      error: () => {
        this.attachmentError.set('No se pudo eliminar el adjunto.');
      },
    });
  }

  // Re-expuesto como metodo porque el template no puede llamar
  // funciones importadas directamente.
  formatFileSize = formatFileSize;

  // --- Comentarios ---

  showComments = signal(false);
  loadingComments = signal(false);
  postingComment = signal(false);
  comments = signal<TaskCommentDto[]>([]);
  commentError = signal<string | null>(null);
  newCommentText = '';

  toggleComments(): void {
    const opening = !this.showComments();
    this.showComments.set(opening);

    if (opening && this.comments().length === 0) {
      this.loadComments();
    }
  }

  private loadComments(): void {
    this.loadingComments.set(true);
    this.commentService.getAll(this.task.id).subscribe({
      next: (list) => {
        this.comments.set(list);
        this.loadingComments.set(false);
      },
      error: () => {
        this.loadingComments.set(false);
        this.commentError.set('No se pudieron cargar los comentarios.');
      },
    });
  }

  onPostComment(): void {
    const content = this.newCommentText.trim();
    if (!content) return;

    this.commentError.set(null);
    this.postingComment.set(true);

    this.commentService.create(this.task.id, content).subscribe({
      next: (posted) => {
        this.comments.update((list) => [...list, posted]);
        this.newCommentText = '';
        this.postingComment.set(false);
      },
      error: () => {
        this.postingComment.set(false);
        this.commentError.set('No se pudo publicar el comentario.');
      },
    });
  }

  onDeleteComment(comment: TaskCommentDto): void {
    this.commentService.delete(this.task.id, comment.id).subscribe({
      next: () => {
        this.comments.update((list) => list.filter((c) => c.id !== comment.id));
      },
      error: () => {
        this.commentError.set('No se pudo eliminar el comentario.');
      },
    });
  }
}
