import { Component, OnInit, OnDestroy, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
// FormsModule (ngModel) convive con ReactiveFormsModule: el form de
// crear tarea usa reactive, y la edicion del proyecto usa ngModel sobre
// campos sueltos fuera de cualquier formGroup. No se mezclan en el
// mismo control, que es lo unico que Angular no permite.
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subscription, forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatTabsModule } from '@angular/material/tabs';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { TaskService, TaskItemDto, TaskItemStatus } from '../../../core/services/task.service';
import { ProjectService, ProjectDto } from '../../../core/services/project.service';
import {
  ProjectMemberService,
  ProjectMemberDto,
} from '../../../core/services/project-member.service';
import { UserService, UserDto } from '../../../core/services/user.service';
import { AuthService } from '../../../core/services/auth.service';
import { TareasTablaComponent } from '../../../shared/components/tareas-tabla/tareas-tabla.component';
import { ProyectoEstadisticasComponent } from '../proyecto-estadisticas/proyecto-estadisticas.component';
import { ProyectoArchivosComponent } from '../proyecto-archivos/proyecto-archivos.component';
import { EditorTextoComponent } from '../../../shared/components/editor-texto/editor-texto.component';
import { TareaDetalleDialogComponent } from '../../../shared/components/tarea-detalle-dialog/tarea-detalle-dialog.component';
import { RealtimeService } from '../../../core/services/realtime.service';

@Component({
  selector: 'app-proyecto-detalle',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    ReactiveFormsModule,
    FormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatProgressSpinnerModule,
    MatChipsModule,
    MatTabsModule,
    MatDialogModule,
    TareasTablaComponent,
    ProyectoEstadisticasComponent,
    ProyectoArchivosComponent,
    EditorTextoComponent,
  ],
  templateUrl: './proyecto-detalle.component.html',
  styleUrl: './proyecto-detalle.component.scss',
})
export class ProyectoDetalleComponent implements OnInit, OnDestroy {
  projectId = '';

  project = signal<ProjectDto | null>(null);
  loadingProject = signal(true);

  tasks = signal<TaskItemDto[]>([]);
  loadingTasks = signal(true);
  showCreateTaskForm = signal(false);
  creatingTask = signal(false);

  members = signal<ProjectMemberDto[]>([]);
  loadingMembers = signal(true);
  showInviteForm = signal(false);
  inviteCandidates = signal<UserDto[]>([]);
  selectedInviteeId = signal<string | null>(null);
  inviting = signal(false);

  errorMessage = signal<string | null>(null);

  // Solo el dueno del proyecto puede invitar/quitar miembros.
  // Esto es solo UX: la validacion real vuelve a ocurrir en el backend.
  isOwner = computed(() => {
    const p = this.project();
    return p !== null && p.ownerId === this.auth.getUserId();
  });

  taskForm! :ReturnType<FormBuilder['group']>;

  constructor(
    private route: ActivatedRoute,
    private fb: FormBuilder,
    private taskService: TaskService,
    private projectService: ProjectService,
    private memberService: ProjectMemberService,
    private userService: UserService,
    private auth: AuthService,
    private dialog: MatDialog,
    private realtime: RealtimeService
  ) {this.taskForm = this.fb.group({
    title: ['', Validators.required],
    description: [''],
    // Puede quedar vacio (tarea sin asignar) o traer varias personas:
    // en ese caso se crea una tarea independiente por cada una.
    assignedToIds: [[] as string[]],
  });}

  private realtimeSub?: Subscription;

  ngOnInit(): void {
    this.projectId = this.route.snapshot.paramMap.get('id') ?? '';
    this.loadProject();
    this.loadTasks();
    this.loadMembers();

    // Cualquiera con este proyecto abierto ve en vivo los cambios que
    // haga otra persona (crear tarea, cambiar estado, reasignar) sin
    // tener que refrescar. Ver RealtimeService y TaskHub en el backend.
    this.realtime.joinProject(this.projectId);
    this.realtimeSub = this.realtime.taskChanged$.subscribe((t) => this.applyRealtimeUpdate(t));
  }

  ngOnDestroy(): void {
    this.realtime.leaveProject(this.projectId);
    this.realtimeSub?.unsubscribe();
  }

  // --- Edicion del proyecto (nombre + descripcion) ---
  // Solo el dueno: el backend lo valida igual, esto es solo UX.

  editandoProyecto = signal(false);
  guardandoProyecto = signal(false);
  edicionNombre = '';
  edicionDescripcion: string | null = null;

  entrarEdicionProyecto(): void {
    const p = this.project();
    if (!p) return;

    this.edicionNombre = p.name;
    this.edicionDescripcion = p.description;
    this.editandoProyecto.set(true);
  }

  cancelarEdicionProyecto(): void {
    this.editandoProyecto.set(false);
  }

  guardarEdicionProyecto(): void {
    const nombre = this.edicionNombre.trim();
    if (!nombre) return;

    this.guardandoProyecto.set(true);

    this.projectService.update(this.projectId, nombre, this.edicionDescripcion).subscribe({
      next: (actualizado) => {
        this.project.set(actualizado);
        this.guardandoProyecto.set(false);
        this.editandoProyecto.set(false);
      },
      error: () => {
        this.guardandoProyecto.set(false);
        this.errorMessage.set('No se pudo actualizar el proyecto.');
      },
    });
  }

  loadProject(): void {
    this.loadingProject.set(true);
    this.projectService.getById(this.projectId).subscribe({
      next: (project) => {
        this.project.set(project);
        this.loadingProject.set(false);
      },
      error: () => {
        this.errorMessage.set('No se pudo cargar el proyecto.');
        this.loadingProject.set(false);
      },
    });
  }

  loadTasks(): void {
    this.loadingTasks.set(true);
    this.taskService.getByProject(this.projectId).subscribe({
      next: (tasks) => {
        this.tasks.set(tasks);
        this.loadingTasks.set(false);
      },
      error: () => {
        this.errorMessage.set('No se pudieron cargar las tareas.');
        this.loadingTasks.set(false);
      },
    });
  }

  loadMembers(): void {
    this.loadingMembers.set(true);
    this.memberService.getAll(this.projectId).subscribe({
      next: (members) => {
        this.members.set(members);
        this.loadingMembers.set(false);
      },
      error: () => {
        this.errorMessage.set('No se pudieron cargar los miembros.');
        this.loadingMembers.set(false);
      },
    });
  }

  toggleCreateTaskForm(): void {
    this.showCreateTaskForm.update((v) => !v);
  }

  onCreateTask(): void {
    if (this.taskForm.invalid) {
      this.taskForm.markAllAsTouched();
      return;
    }

    this.creatingTask.set(true);

    const title = this.taskForm.value.title!;
    const description = this.taskForm.value.description || null;
    const assignedToIds: string[] = this.taskForm.value.assignedToIds || [];

    // Si no se elige a nadie, se crea una sola tarea sin asignar. Si se
    // eligen varias personas, no es una tarea compartida: se crea una
    // tarea independiente por cada una (mismo titulo/descripcion).
    const assignments: (string | null)[] = assignedToIds.length > 0 ? assignedToIds : [null];

    const creations = assignments.map((assignedToId) =>
      this.taskService
        .create({
          title,
          description,
          projectId: this.projectId,
          assignedToId,
          parentTaskId: null,
        })
        .pipe(catchError(() => of(null)))
    );

    forkJoin(creations).subscribe((results) => {
      const newTasks = results.filter((t): t is TaskItemDto => t !== null);
      this.tasks.update((list) => [...newTasks, ...list]);
      this.creatingTask.set(false);

      if (newTasks.length < results.length) {
        this.errorMessage.set(
          `Se crearon ${newTasks.length} de ${results.length} tareas. Revisa e intenta de nuevo con las que fallaron.`
        );
      } else {
        this.showCreateTaskForm.set(false);
        this.taskForm.reset();
      }
    });
  }

  // onDone: se usa desde el modal de detalle para refrescar la tarea que
  // esta mostrando una vez que el cambio ya se guardo (ver openTaskDetail).
  onStatusChange(task: TaskItemDto, newStatus: TaskItemStatus, onDone?: () => void): void {
    this.taskService.updateStatus(task.id, newStatus).subscribe({
      next: (updated) => {
        this.replaceTask(updated);
        onDone?.();
      },
      error: () => {
        this.errorMessage.set('No se pudo actualizar el estado de la tarea.');
      },
    });
  }

  onAssigneeChange(task: TaskItemDto, newAssignedToId: string | null, onDone?: () => void): void {
    this.taskService.updateAssignee(task.id, newAssignedToId).subscribe({
      next: (updated) => {
        this.replaceTask(updated);
        onDone?.();
      },
      error: () => {
        this.errorMessage.set('No se pudo actualizar el asignado de la tarea.');
      },
    });
  }

  onTaskDetailsChange(
    task: TaskItemDto,
    data: { title: string; description: string | null },
    onDone?: () => void
  ): void {
    this.taskService.updateDetails(task.id, data.title, data.description).subscribe({
      next: (updated) => {
        this.replaceTask(updated);
        onDone?.();
      },
      error: () => {
        this.errorMessage.set('No se pudo actualizar la tarea.');
      },
    });
  }

  // Reemplaza una tarea por su version actualizada, sin importar si es
  // top-level o una subtarea colgada de otra. Los endpoints de
  // status/assign no vuelven a traer las subtareas de la tarea que
  // tocan, asi que se preservan las que ya estaban cargadas localmente.
  private replaceTask(updated: TaskItemDto): void {
    this.tasks.update((list) =>
      list.map((t) => {
        if (t.id === updated.id) {
          return { ...updated, subtasks: t.subtasks };
        }
        if (t.subtasks.some((s) => s.id === updated.id)) {
          return { ...t, subtasks: t.subtasks.map((s) => (s.id === updated.id ? updated : s)) };
        }
        return t;
      })
    );
  }

  // Aplica un TaskChanged que llego por SignalR (ver ngOnInit): a
  // diferencia de replaceTask (que solo actualiza algo que YO acabo de
  // cambiar), esto tiene que cubrir tareas nuevas que otra persona creo
  // mientras tenia este proyecto abierto, no solo actualizaciones.
  private applyRealtimeUpdate(updated: TaskItemDto): void {
    if (updated.projectId !== this.projectId) return;

    this.tasks.update((list) => {
      const yaExiste = list.some(
        (t) => t.id === updated.id || t.subtasks.some((s) => s.id === updated.id)
      );

      if (yaExiste) {
        return list.map((t) => {
          if (t.id === updated.id) return { ...updated, subtasks: t.subtasks };
          if (t.subtasks.some((s) => s.id === updated.id)) {
            return { ...t, subtasks: t.subtasks.map((s) => (s.id === updated.id ? updated : s)) };
          }
          return t;
        });
      }

      // Tarea nueva. Si es una subtarea de algo que ya tengo cargado, se
      // cuelga ahi; si el padre no esta cargado (no deberia pasar dentro
      // del mismo proyecto, pero por si acaso) se ignora — aparecera en
      // el proximo refresh manual.
      if (updated.parentTaskId) {
        const tienePadre = list.some((t) => t.id === updated.parentTaskId);
        if (!tienePadre) return list;

        return list.map((t) =>
          t.id === updated.parentTaskId ? { ...t, subtasks: [...t.subtasks, updated] } : t
        );
      }

      return [updated, ...list];
    });
  }

  // Transicion automatica Asignada -> Leida: se dispara al abrir el
  // modal, no es algo que el usuario elija. Solo aplica si quien abre
  // es la persona asignada y la tarea sigue en "Asignada" (si ya la leyo
  // antes, o si es otro estado, no hace nada). Falla en silencio: no es
  // una accion que el usuario disparo a mano, no vale la pena mostrar un
  // error si no hay conexion (se reintenta la proxima vez que la abra).
  private markTaskReadIfNeeded(task: TaskItemDto, onDone: () => void): void {
    if (task.assignedToId !== this.auth.getUserId() || task.status !== TaskItemStatus.Asignada) {
      return;
    }
    this.taskService.markRead(task.id).subscribe({
      next: (updated) => {
        this.replaceTask(updated);
        onDone();
      },
      error: () => {},
    });
  }

  onSubtaskCreate(
    parent: TaskItemDto,
    data: { title: string; assignedToId: string | null },
    onDone?: () => void
  ): void {
    this.taskService
      .create({
        title: data.title,
        description: null,
        projectId: this.projectId,
        assignedToId: data.assignedToId,
        parentTaskId: parent.id,
      })
      .subscribe({
        next: (newSubtask) => {
          this.tasks.update((list) =>
            list.map((t) =>
              t.id === parent.id ? { ...t, subtasks: [...t.subtasks, newSubtask] } : t
            )
          );
          onDone?.();
        },
        error: () => {
          this.errorMessage.set('No se pudo crear la subtarea.');
        },
      });
  }

  // Abre el modal de detalle con la tarjeta completa (estado, asignado,
  // subtareas, adjuntos). Reusa los mismos handlers que antes se
  // llamaban desde la tarjeta inline en la lista; despues de cada cambio
  // exitoso, refresca lo que el modal esta mostrando con la version mas
  // reciente de `tasks` (unica fuente de verdad).
  openTaskDetail(task: TaskItemDto): void {
    const ref = this.dialog.open(TareaDetalleDialogComponent, {
      data: { task, members: this.members(), showProject: false, isProjectOwner: this.isOwner() },
      width: '860px', // ~20% mas ancho que antes (720px)
      maxWidth: '96vw',
      panelClass: 'tarea-detalle-panel',
      autoFocus: false,
    });

    const instance = ref.componentInstance;
    const refresh = () => {
      const latest = this.tasks().find((t) => t.id === task.id);
      if (latest) instance.task.set(latest);
    };

    this.markTaskReadIfNeeded(task, refresh);

    instance.statusChange.subscribe((v) => this.onStatusChange(task, v, refresh));
    instance.assigneeChange.subscribe((v) => this.onAssigneeChange(task, v, refresh));
    instance.subtaskStatusChange.subscribe((v) => this.onStatusChange(v.subtask, v.status, refresh));
    instance.subtaskAssigneeChange.subscribe((v) =>
      this.onAssigneeChange(v.subtask, v.assignedToId, refresh)
    );
    instance.subtaskCreate.subscribe((v) => this.onSubtaskCreate(task, v, refresh));
    instance.detailsChange.subscribe((v) => this.onTaskDetailsChange(task, v, refresh));
    instance.subtaskDetailsChange.subscribe((v) =>
      this.onTaskDetailsChange(v.subtask, { title: v.title, description: v.description }, refresh)
    );
  }

  toggleInviteForm(): void {
    const opening = !this.showInviteForm();
    this.showInviteForm.set(opening);

    if (opening) {
      this.loadInviteCandidates();
    }
  }

  private loadInviteCandidates(): void {
    const project = this.project();
    if (!project) return;

    this.userService.getByArea(project.areaId).subscribe({
      next: (users) => {
        // Excluye a quienes ya son miembros
        const memberIds = new Set(this.members().map((m) => m.userId));
        this.inviteCandidates.set(users.filter((u) => !memberIds.has(u.id)));
      },
      error: () => {
        this.errorMessage.set('No se pudo cargar el catálogo de usuarios del área.');
      },
    });
  }

  onInvite(): void {
    const userId = this.selectedInviteeId();
    if (!userId) return;

    this.inviting.set(true);

    this.memberService.add(this.projectId, userId).subscribe({
      next: (newMember) => {
        this.members.update((list) => [...list, newMember]);
        this.inviteCandidates.update((list) => list.filter((u) => u.id !== userId));
        this.selectedInviteeId.set(null);
        this.inviting.set(false);
      },
      error: () => {
        this.inviting.set(false);
        this.errorMessage.set('No se pudo invitar al usuario.');
      },
    });
  }

  onRemoveMember(member: ProjectMemberDto): void {
    this.memberService.remove(this.projectId, member.userId).subscribe({
      next: () => {
        this.members.update((list) => list.filter((m) => m.userId !== member.userId));
      },
      error: () => {
        this.errorMessage.set('No se pudo quitar al miembro.');
      },
    });
  }
}