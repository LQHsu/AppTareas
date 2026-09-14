import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { FolderService, FolderDto } from '../../core/services/folder.service';
import { UserService, UserDto } from '../../core/services/user.service';
import { CompartirCarpetaDialogComponent } from '../../shared/components/compartir-carpeta-dialog/compartir-carpeta-dialog.component';

// Vista dedicada de carpetas, en tarjetas. Aqui vive TODA la
// administracion (crear, renombrar, compartir, eliminar); en /proyectos
// las carpetas solo sirven de filtro, para no duplicar esta logica en
// dos pantallas.
@Component({
  selector: 'app-carpetas',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatTooltipModule,
    MatProgressSpinnerModule,
    MatDialogModule,
  ],
  templateUrl: './carpetas.component.html',
  styleUrl: './carpetas.component.scss',
})
export class CarpetasComponent implements OnInit {
  folders = signal<FolderDto[]>([]);
  loading = signal(true);
  errorMessage = signal<string | null>(null);
  mensajeExito = signal<string | null>(null);

  creando = signal(false);
  nuevoNombre = '';

  propias = computed(() => this.folders().filter((f) => f.isOwner));
  compartidasConmigo = computed(() => this.folders().filter((f) => !f.isOwner));

  constructor(
    private folderService: FolderService,
    private userService: UserService,
    private dialog: MatDialog,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.folderService.getMine().subscribe({
      next: (folders) => {
        this.folders.set(folders);
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('No se pudieron cargar las carpetas.');
        this.loading.set(false);
      },
    });
  }

  // Ver los proyectos de una carpeta = /proyectos con el filtro puesto.
  verProyectos(folder: FolderDto): void {
    this.router.navigate(['/proyectos'], { queryParams: { carpeta: folder.id } });
  }

  toggleCrear(): void {
    this.nuevoNombre = '';
    this.creando.update((v) => !v);
  }

  onCrear(): void {
    const nombre = this.nuevoNombre.trim();
    if (!nombre) return;

    this.folderService.create(nombre).subscribe({
      next: (folder) => {
        this.folders.update((list) =>
          [...list, folder].sort((a, b) => a.name.localeCompare(b.name))
        );
        this.creando.set(false);
        this.nuevoNombre = '';
      },
      error: () => this.errorMessage.set('No se pudo crear la carpeta.'),
    });
  }

  onRenombrar(folder: FolderDto): void {
    const nombre = window.prompt('Nuevo nombre de la carpeta', folder.name)?.trim();
    if (!nombre || nombre === folder.name) return;

    this.folderService.rename(folder.id, nombre).subscribe({
      next: (actualizada) => this.reemplazar(actualizada),
      error: () => this.errorMessage.set('No se pudo renombrar la carpeta.'),
    });
  }

  // Borrar la carpeta NO borra los proyectos: quedan sin carpeta.
  onEliminar(folder: FolderDto): void {
    const ok = window.confirm(
      `¿Eliminar la carpeta «${folder.name}»?\n\n` +
        `Sus ${folder.projectCount} proyecto(s) NO se eliminan: quedan sin carpeta.`
    );
    if (!ok) return;

    this.folderService.delete(folder.id).subscribe({
      next: () => this.folders.update((list) => list.filter((f) => f.id !== folder.id)),
      error: () => this.errorMessage.set('No se pudo eliminar la carpeta.'),
    });
  }

  onCompartir(folder: FolderDto): void {
    const me = this.userService.currentUser();
    if (!me) return;

    // Candidatos: gente de mi area, menos yo. Misma regla que invitar a
    // un proyecto; el backend la vuelve a validar.
    this.userService.getByArea(me.areaId).subscribe({
      next: (usuarios) => {
        const candidatos: UserDto[] = usuarios.filter((u) => u.id !== me.id);

        this.dialog
          .open(CompartirCarpetaDialogComponent, {
            data: { folder, candidatos },
            width: '624px', // ~20% mas ancho que antes (520px)
            maxWidth: '95vw',
            autoFocus: false,
          })
          .afterClosed()
          .subscribe((userId?: string) => {
            if (userId) this.ejecutarCompartir(folder, userId);
          });
      },
      error: () => this.errorMessage.set('No se pudo cargar la gente de tu área.'),
    });
  }

  private ejecutarCompartir(folder: FolderDto, userId: string): void {
    this.folderService.share(folder.id, userId).subscribe({
      next: (resultado) => {
        this.reemplazar(resultado.folder);
        this.errorMessage.set(null);
        this.mensajeExito.set(
          `Carpeta compartida con ${resultado.folder.sharedWithFullName}. ` +
            `Se reasignaron ${resultado.reassignedTasks} tarea(s).`
        );
      },
      error: (err) =>
        this.errorMessage.set(
          typeof err?.error === 'string' ? err.error : 'No se pudo compartir la carpeta.'
        ),
    });
  }

  // Dejar de compartir no revierte las tareas: siguen con quien las tenga.
  onDejarDeCompartir(folder: FolderDto): void {
    const ok = window.confirm(
      `¿Dejar de compartir «${folder.name}» con ${folder.sharedWithFullName}?\n\n` +
        `Las tareas que se le reasignaron siguen siendo suyas: esto solo le quita el acceso.`
    );
    if (!ok) return;

    this.folderService.share(folder.id, null).subscribe({
      next: (resultado) => this.reemplazar(resultado.folder),
      error: () => this.errorMessage.set('No se pudo dejar de compartir la carpeta.'),
    });
  }

  private reemplazar(folder: FolderDto): void {
    this.folders.update((list) => list.map((f) => (f.id === folder.id ? folder : f)));
  }
}
