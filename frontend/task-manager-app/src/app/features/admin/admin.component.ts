import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTableModule } from '@angular/material/table';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule, MatSlideToggleChange } from '@angular/material/slide-toggle';
import { AdminService, AdminProjectDto } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';
import { UserDto } from '../../core/services/user.service';

// Panel de super admin: gestion global de usuarios (ver a todos,
// promover/degradar super admin) y vista global de proyectos de solo
// lectura. Protegido por superAdminGuard en las rutas.
@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [
    CommonModule,
    MatIconModule,
    MatTabsModule,
    MatTableModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
  ],
  templateUrl: './admin.component.html',
  styleUrl: './admin.component.scss',
})
export class AdminComponent implements OnInit {
  users = signal<UserDto[]>([]);
  loadingUsers = signal(true);
  updatingUserId = signal<string | null>(null);

  projects = signal<AdminProjectDto[]>([]);
  loadingProjects = signal(true);

  errorMessage = signal<string | null>(null);

  userColumns = ['fullName', 'email', 'area', 'superAdmin', 'banned'];
  projectColumns = ['name', 'area', 'owner', 'members', 'tasks', 'createdAt'];

  constructor(
    private adminService: AdminService,
    private auth: AuthService
  ) {}

  ngOnInit(): void {
    this.loadUsers();
    this.loadProjects();
  }

  loadUsers(): void {
    this.loadingUsers.set(true);
    this.adminService.getAllUsers().subscribe({
      next: (users) => {
        this.users.set(users);
        this.loadingUsers.set(false);
      },
      error: () => {
        this.errorMessage.set('No se pudo cargar la lista de usuarios.');
        this.loadingUsers.set(false);
      },
    });
  }

  loadProjects(): void {
    this.loadingProjects.set(true);
    this.adminService.getAllProjects().subscribe({
      next: (projects) => {
        this.projects.set(projects);
        this.loadingProjects.set(false);
      },
      error: () => {
        this.errorMessage.set('No se pudo cargar la lista de proyectos.');
        this.loadingProjects.set(false);
      },
    });
  }

  isSelf(user: UserDto): boolean {
    return user.id === this.auth.getUserId();
  }

  onToggleSuperAdmin(user: UserDto, event: MatSlideToggleChange): void {
    const newValue = event.checked;
    this.updatingUserId.set(user.id);

    this.adminService.setSuperAdmin(user.id, newValue).subscribe({
      next: (updated) => {
        this.users.update((list) => list.map((u) => (u.id === updated.id ? updated : u)));
        this.updatingUserId.set(null);
      },
      error: (err) => {
        // Revierte el toggle visualmente: la lista no cambio, asi que
        // al no actualizar `users` el checked vuelve a su valor real.
        event.source.checked = user.isSuperAdmin;
        this.updatingUserId.set(null);
        this.errorMessage.set(
          err?.status === 400
            ? 'No puedes quitarte a ti mismo: eres el único super admin que queda.'
            : 'No se pudo actualizar el usuario.'
        );
      },
    });
  }

  onToggleBanned(user: UserDto, event: MatSlideToggleChange): void {
    const newValue = event.checked;
    this.updatingUserId.set(user.id);

    this.adminService.setBanned(user.id, newValue).subscribe({
      next: (updated) => {
        this.users.update((list) => list.map((u) => (u.id === updated.id ? updated : u)));
        this.updatingUserId.set(null);
      },
      error: () => {
        event.source.checked = user.isBanned;
        this.updatingUserId.set(null);
        this.errorMessage.set('No se pudo actualizar el usuario.');
      },
    });
  }
}
