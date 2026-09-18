import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink, RouterLinkActive, RouterOutlet, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ProjectService, ProjectDto } from '../../../core/services/project.service';
import { TaskService, TaskItemDto } from '../../../core/services/task.service';
import { UserService } from '../../../core/services/user.service';
import { RealtimeService } from '../../../core/services/realtime.service';
import { AuthService } from '../../../core/services/auth.service';

// Layout persistente para toda la app autenticada (ver app.routes.ts:
// envuelve proyectos/proyectos:id/mis-tareas/admin como hijos). Colapsado
// muestra solo iconos; un boton lo fija expandido, mostrando ademas
// accesos directos a todos los proyectos y a las ultimas 5 tareas.
//
// Los datos de esos accesos (projects/recentTasks) se recargan en cada
// NavigationEnd -no es un estado reactivo compartido de verdad, no hay
// store global en este proyecto- asi que si creas un proyecto o tarea
// nueva se refleja en el siguiente cambio de ruta, no al instante.
@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    MatSidenavModule,
    MatIconModule,
    MatButtonModule,
    MatTooltipModule,
  ],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
})
export class AppShellComponent implements OnInit {
  expanded = signal(false);

  projects = signal<ProjectDto[]>([]);
  loadingProjects = signal(true);

  recentTasks = signal<TaskItemDto[]>([]);
  loadingTasks = signal(true);

  constructor(
    private router: Router,
    private projectService: ProjectService,
    private taskService: TaskService,
    public userService: UserService,
    private realtime: RealtimeService,
    private auth: AuthService
  ) {}

  ngOnInit(): void {
    // Se necesita en cualquier pantalla (no solo /proyectos) para saber
    // si se muestra el link de Admin, asi que se pide aca en vez de en
    // cada pagina individual.
    this.userService.getMe().subscribe();

    // Una sola conexion para toda la sesion, aca porque el shell envuelve
    // cualquier pantalla autenticada. proyecto-detalle/mis-tareas solo se
    // suscriben a los eventos (taskChanged$) y, en el caso de
    // proyecto-detalle, se unen/salen del grupo de SU proyecto.
    this.realtime.start();

    this.loadProjects();
    this.loadRecentTasks();

    this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => {
      this.loadProjects();
      this.loadRecentTasks();
    });
  }

  toggleExpanded(): void {
    this.expanded.update((v) => !v);
  }

  logout(): void {
    this.auth.logout();
  }

  loadProjects(): void {
    this.loadingProjects.set(true);
    this.projectService.getMine().subscribe({
      next: (projects) => {
        this.projects.set(projects);
        this.loadingProjects.set(false);
      },
      error: () => this.loadingProjects.set(false),
    });
  }

  loadRecentTasks(): void {
    this.loadingTasks.set(true);
    this.taskService.getMine().subscribe({
      next: (tasks) => {
        this.recentTasks.set(tasks.slice(0, 5));
        this.loadingTasks.set(false);
      },
      error: () => this.loadingTasks.set(false),
    });
  }

  // Reusa proyecto-detalle o mis-tareas para ver el detalle en vez de
  // reproducir la logica del modal (miembros, permisos) aca.
  onRecentTaskClick(task: TaskItemDto): void {
    if (task.projectId) {
      this.router.navigate(['/proyectos', task.projectId]);
    } else {
      this.router.navigate(['/mis-tareas']);
    }
  }
}
