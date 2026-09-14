import { Component, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subscription, forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { TaskService, TaskItemDto, TaskItemStatus } from '../../core/services/task.service';
import { UserService, UserDto } from '../../core/services/user.service';
import { TareasTablaComponent } from '../../shared/components/tareas-tabla/tareas-tabla.component';
import { TareaDetalleDialogComponent } from '../../shared/components/tarea-detalle-dialog/tarea-detalle-dialog.component';
import { EditorTextoComponent } from '../../shared/components/editor-texto/editor-texto.component';
import { RealtimeService } from '../../core/services/realtime.service';

// Vista personal: todo lo asignado al usuario, sin importar de que
// proyecto venga (o si es una tarea suelta). A diferencia de
// proyecto-detalle, aqui no se conoce una unica lista de miembros con
// quien reasignar, asi que el campo "Asignada a" se muestra de solo
// lectura (ver TareaCardComponent).
//
// Tambien es donde se crean las tareas sueltas: se asignan dentro de la
// misma area del creador (regla de negocio validada en el backend).
@Component({
  selector: 'app-mis-tareas',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatProgressSpinnerModule,
    MatDialogModule,
    TareasTablaComponent,
    EditorTextoComponent,
  ],
  templateUrl: './mis-tareas.component.html',
  styleUrl: './mis-tareas.component.scss',
})
export class MisTareasComponent implements OnInit, OnDestroy {
  tasks = signal<TaskItemDto[]>([]);
  loading = signal(true);
  errorMessage = signal<string | null>(null);

  showCreateTaskForm = signal(false);
  creatingTask = signal(false);
  areaCandidates = signal<UserDto[]>([]);

  taskForm!: ReturnType<FormBuilder['group']>;

  constructor(
    private fb: FormBuilder,
    private taskService: TaskService,
    private userService: UserService,
    private dialog: MatDialog,
    private realtime: RealtimeService
  ) {
    this.taskForm = this.fb.group({
      title: ['', Validators.required],
      description: [''],
      assignedToIds: [[] as string[]],
    });
  }

  private realtimeSub?: Subscription;

  ngOnInit(): void {
    this.loadTasks();
    this.loadCurrentUserArea();

    // A diferencia de proyecto-detalle, aca no hace falta unirse a
    // ningun grupo: el backend ya une la conexion al grupo personal del
    // usuario al conectar (ver TaskHub.OnConnectedAsync), que es
    // justo lo que "Mis tareas" necesita (asignadas a mi o creadas por
    // mi, sin importar el proyecto).
    this.realtimeSub = this.realtime.taskChanged$.subscribe((t) => this.applyRealtimeUpdate(t));
  }

  ngOnDestroy(): void {
    this.realtimeSub?.unsubscribe();
  }

  // Si la tarea ahora me pertenece (asignada o creada por mi), la
  // agrega/actualiza; si ya la tenia pero dejo de ser mia (se
  // reasigno a alguien mas), la quita de la lista.
  private applyRealtimeUpdate(updated: TaskItemDto): void {
    const myId = this.userService.currentUser()?.id;
    const esMia = myId !== undefined && (updated.assignedToId === myId || updated.createdById === myId);

    this.tasks.update((list) => {
      const yaEstaba = list.some((t) => t.id === updated.id);

      if (esMia) {
        return yaEstaba ? list.map((t) => (t.id === updated.id ? updated : t)) : [updated, ...list];
      }

      return yaEstaba ? list.filter((t) => t.id !== updated.id) : list;
    });
  }

  loadTasks(): void {
    this.loading.set(true);
    this.taskService.getMine().subscribe({
      next: (tasks) => {
        this.tasks.set(tasks);
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('No se pudieron cargar tus tareas.');
        this.loading.set(false);
      },
    });
  }

  private loadCurrentUserArea(): void {
    this.userService.getMe().subscribe((me) => {
      if (!me) return;
      this.userService.getByArea(me.areaId).subscribe({
        next: (users) => this.areaCandidates.set(users),
        error: () => {
          this.errorMessage.set('No se pudo cargar el catálogo de usuarios del área.');
        },
      });
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

    // Igual que en proyecto-detalle: sin nadie seleccionado se crea una
    // sola tarea suelta sin asignar; con varias personas, una tarea
    // suelta independiente por cada una.
    const assignments: (string | null)[] = assignedToIds.length > 0 ? assignedToIds : [null];

    const creations = assignments.map((assignedToId) =>
      this.taskService
        .create({
          title,
          description,
          projectId: null,
          assignedToId,
          parentTaskId: null,
        })
        .pipe(catchError(() => of(null)))
    );

    forkJoin(creations).subscribe((results) => {
      const newTasks = results.filter((t): t is TaskItemDto => t !== null);
      // Se agregan a la vista aunque no esten asignadas a mi: es util
      // ver de inmediato lo que se acaba de crear. Un refresh (GetMine)
      // las quitaria si no me las asigne a mi mismo.
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
        this.tasks.update((list) =>
          list.map((t) => (t.id === updated.id ? updated : t))
        );
        onDone?.();
      },
      error: () => {
        this.errorMessage.set('No se pudo actualizar el estado de la tarea.');
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
        this.tasks.update((list) => list.map((t) => (t.id === updated.id ? updated : t)));
        onDone?.();
      },
      error: () => {
        this.errorMessage.set('No se pudo actualizar la tarea.');
      },
    });
  }

  // Aqui las tareas nunca traen subtareas (GetMine es plano) y el
  // asignado se muestra de solo lectura (sin `members`), asi que solo
  // hacen falta statusChange y detailsChange.
  openTaskDetail(task: TaskItemDto): void {
    const ref = this.dialog.open(TareaDetalleDialogComponent, {
      data: { task, members: null, showProject: true },
      width: '860px', // ~20% mas ancho que antes (720px)
      maxWidth: '96vw',
      panelClass: 'tarea-detalle-panel',
      autoFocus: false,
    });

    const refresh = () => {
      const latest = this.tasks().find((t) => t.id === task.id);
      if (latest) ref.componentInstance.task.set(latest);
    };

    this.markTaskReadIfNeeded(task, refresh);

    ref.componentInstance.statusChange.subscribe((v) => this.onStatusChange(task, v, refresh));
    ref.componentInstance.detailsChange.subscribe((v) =>
      this.onTaskDetailsChange(task, v, refresh)
    );
  }

  // Transicion automatica Asignada -> Leida al abrir el modal (ver mismo
  // metodo en proyecto-detalle). Falla en silencio: no es una accion que
  // el usuario disparo a mano.
  private markTaskReadIfNeeded(task: TaskItemDto, onDone: () => void): void {
    if (task.assignedToId !== this.userService.currentUser()?.id || task.status !== TaskItemStatus.Asignada) {
      return;
    }
    this.taskService.markRead(task.id).subscribe({
      next: (updated) => {
        this.tasks.update((list) => list.map((t) => (t.id === updated.id ? updated : t)));
        onDone();
      },
      error: () => {},
    });
  }
}
