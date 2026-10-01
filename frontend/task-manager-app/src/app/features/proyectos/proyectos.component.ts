import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ProjectService, ProjectDto, ProjectTrashItemDto } from '../../core/services/project.service';
import { FolderService, FolderDto } from '../../core/services/folder.service';
import { AuthService } from '../../core/services/auth.service';
import { EditorTextoComponent } from '../../shared/components/editor-texto/editor-texto.component';
import { ColorPickerComponent } from '../../shared/components/color-picker/color-picker.component';
import { TextoPlanoPipe } from '../../shared/pipes/texto-plano.pipe';
import { bannerColor, contrastTextColor } from '../../shared/color-palette';

// Filtro de la barra de carpetas: 'all' = todos los proyectos,
// 'none' = los que no estan en ninguna carpeta, o el id de una carpeta.
type FiltroCarpeta = 'all' | 'none' | string;

@Component({
  selector: 'app-proyectos',
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
    MatProgressSpinnerModule,
    MatProgressBarModule,
    MatMenuModule,
    MatTooltipModule,
    EditorTextoComponent,
    ColorPickerComponent,
    TextoPlanoPipe,
  ],
  templateUrl: './proyectos.component.html',
  styleUrl: './proyectos.component.scss',
})
export class ProyectosComponent implements OnInit {
  projects = signal<ProjectDto[]>([]);
  loading = signal(true);
  showCreateForm = signal(false);
  creating = signal(false);
  errorMessage = signal<string | null>(null);

  // --- Carpetas ---
  // Aqui solo se usan como filtro y como destino para mover proyectos.
  // Crear/renombrar/compartir/eliminar vive en /carpetas, para no tener
  // la misma logica duplicada en dos pantallas.
  folders = signal<FolderDto[]>([]);
  filtroCarpeta = signal<FiltroCarpeta>('all');

  form!: ReturnType<FormBuilder['group']>;

  // Texto blanco/oscuro segun el color de fondo elegido para el titulo.
  tituloTextColor = contrastTextColor;
  // Color del banner superior de la card (estilo Google Classroom): el
  // propio del proyecto, o un azul por default si no eligio ninguno.
  bannerColor = bannerColor;

  // Solo las carpetas propias sirven como destino para mover proyectos:
  // el backend rechaza mover a una carpeta ajena.
  carpetasPropias = computed(() => this.folders().filter((f) => f.isOwner));

  // El menu "Mover a oficina" solo tiene sentido para el dueno del
  // proyecto: ProjectsController.MoveToFolder rechaza con 403 a
  // cualquier otro miembro (mover proyectos ajenos seria una puerta
  // trasera para invitar gente a un proyecto que no es tuyo). Esto es
  // solo UX -sin este chequeo el boton salia igual para todos y solo
  // fallaba al hacer click-, la validacion real sigue siendo del backend.
  esDueno(project: ProjectDto): boolean {
    return project.ownerId === this.auth.getUserId();
  }

  projectsFiltrados = computed(() => {
    const filtro = this.filtroCarpeta();
    const lista = this.projects();

    const filtrada =
      filtro === 'all'
        ? lista
        : filtro === 'none'
          ? lista.filter((p) => !p.folderId)
          : lista.filter((p) => p.folderId === filtro);

    // Orden alfabetico (sin distinguir acentos ni mayusculas).
    return [...filtrada].sort((a, b) =>
      a.name.localeCompare(b.name, 'es', { sensitivity: 'base' })
    );
  });

  sinCarpetaCount = computed(() => this.projects().filter((p) => !p.folderId).length);

  // Para el aviso en el formulario de creacion: null si el filtro
  // activo no es una carpeta propia (all/none/una compartida contigo).
  carpetaActivaNombre = computed(() => {
    const filtro = this.filtroCarpeta();
    return this.carpetasPropias().find((f) => f.id === filtro)?.name ?? null;
  });

  constructor(
    private fb: FormBuilder,
    private projectService: ProjectService,
    private folderService: FolderService,
    private route: ActivatedRoute,
    private auth: AuthService
  ) {
    this.form = this.fb.group({
      name: ['', Validators.required],
      description: [''],
      color: [null as string | null],
    });
  }

  ngOnInit(): void {
    // /proyectos?carpeta=<id> — asi las tarjetas de /carpetas pueden
    // enlazar directo a "ver los proyectos de esta carpeta".
    const carpeta = this.route.snapshot.queryParamMap.get('carpeta');
    if (carpeta) this.filtroCarpeta.set(carpeta);

    this.loadProjects();
    this.loadFolders();
    this.loadTrash();
  }

  // --- Papelera de proyectos ---
  // Un proyecto con tareas en su historial no se borra de verdad: va aqui
  // y se puede restaurar (con sus tareas). Solo se listan los propios.
  trash = signal<ProjectTrashItemDto[]>([]);
  showTrash = signal(false);

  loadTrash(): void {
    this.projectService.getTrash().subscribe({
      next: (items) => this.trash.set(items),
      error: () => this.errorMessage.set('No se pudo cargar la papelera de proyectos.'),
    });
  }

  onEliminarProyecto(project: ProjectDto, event: Event): void {
    // La tarjeta entera es un routerLink; sin esto, el click navegaria.
    event.stopPropagation();

    const ok = window.confirm(
      `¿Eliminar el proyecto «${project.name}»?\n\n` +
        'Si ya tuvo tareas, se mueve a la papelera y lo puedes restaurar con todo su contenido. ' +
        'Si nunca tuvo tareas, se elimina definitivamente.'
    );
    if (!ok) return;

    this.projectService.delete(project.id).subscribe({
      next: (res) => {
        this.projects.update((list) => list.filter((p) => p.id !== project.id));
        // Cambian los contadores de proyectos de las oficinas.
        this.loadFolders();
        if (!res.permanent) this.loadTrash();
      },
      error: () => this.errorMessage.set('No se pudo eliminar el proyecto.'),
    });
  }

  onRestaurarProyecto(item: ProjectTrashItemDto): void {
    this.projectService.restore(item.id).subscribe({
      next: (restaurado) => {
        this.trash.update((list) => list.filter((t) => t.id !== item.id));
        this.projects.update((list) => [...list, restaurado]);
        this.loadFolders();
      },
      error: () => this.errorMessage.set('No se pudo restaurar el proyecto.'),
    });
  }

  loadProjects(): void {
    this.loading.set(true);
    this.projectService.getMine().subscribe({
      next: (projects) => {
        this.projects.set(projects);
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('No se pudieron cargar tus proyectos.');
        this.loading.set(false);
      },
    });
  }

  loadFolders(): void {
    this.folderService.getMine().subscribe({
      next: (folders) => this.folders.set(folders),
      error: () => this.errorMessage.set('No se pudieron cargar las oficinas.'),
    });
  }

  toggleCreateForm(): void {
    this.showCreateForm.update((v) => !v);
  }

  onCreate(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.creating.set(true);

    // Si estas viendo una carpeta propia (no "all"/"none"/una
    // compartida contigo), el proyecto nuevo nace directo ahi: evita el
    // paso extra de crear y luego mover.
    const folderId = this.carpetaActivaNombre() ? this.filtroCarpeta() : null;

    this.projectService
      .create({
        name: this.form.value.name!,
        description: this.form.value.description || null,
        folderId,
        color: this.form.value.color || null,
      })
      .subscribe({
        next: (newProject) => {
          this.projects.update((list) => [...list, newProject]);
          if (folderId) this.loadFolders(); // actualiza el contador de la carpeta
          this.creating.set(false);
          this.showCreateForm.set(false);
          this.form.reset();
        },
        error: () => {
          this.creating.set(false);
          this.errorMessage.set('No se pudo crear el proyecto. Intenta de nuevo.');
        },
      });
  }

  completionPercent(project: ProjectDto): number {
    if (project.totalTasks === 0) return 0;
    return Math.round((project.completedTasks / project.totalTasks) * 100);
  }

  onMoverProyecto(project: ProjectDto, folderId: string | null, event: Event): void {
    // La tarjeta entera es un routerLink; sin esto, mover navegaria.
    event.stopPropagation();

    this.projectService.moveToFolder(project.id, folderId).subscribe({
      next: (actualizado) => {
        this.projects.update((list) =>
          list.map((p) => (p.id === actualizado.id ? actualizado : p))
        );
        // Cambian los contadores de proyectos/tareas de las carpetas.
        this.loadFolders();
      },
      error: () => this.errorMessage.set('No se pudo mover el proyecto.'),
    });
  }
}
