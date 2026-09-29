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
import { ColorPickerComponent } from '../../shared/components/color-picker/color-picker.component';
import { bannerColor, contrastTextColor } from '../../shared/color-palette';

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
    ColorPickerComponent,
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
  nuevoColor: string | null = null;

  // Edicion inline por-card (nombre + color): solo una carpeta editable
  // a la vez, identificada por id ya que esto es una lista, no un
  // detalle de un solo elemento (a diferencia de proyecto-detalle).
  editandoId = signal<string | null>(null);
  edicionNombre = '';
  edicionColor: string | null = null;

  propias = computed(() => this.folders().filter((f) => f.isOwner));
  compartidasConmigo = computed(() => this.folders().filter((f) => !f.isOwner));

  // Texto blanco/oscuro segun el color de fondo elegido, para que el
  // titulo siga siendo legible con cualquier tono de la paleta.
  tituloTextColor = contrastTextColor;
  // Color del banner superior de la card (estilo Google Classroom): el
  // de la carpeta, o un azul por default si no eligio ninguno.
  bannerColor = bannerColor;

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
        this.errorMessage.set('No se pudieron cargar las oficinas.');
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
    this.nuevoColor = null;
    this.creando.update((v) => !v);
  }

  onCrear(): void {
    const nombre = this.nuevoNombre.trim();
    if (!nombre) return;

    this.folderService.create(nombre, this.nuevoColor).subscribe({
      next: (folder) => {
        this.folders.update((list) =>
          [...list, folder].sort((a, b) => a.name.localeCompare(b.name))
        );
        this.creando.set(false);
        this.nuevoNombre = '';
        this.nuevoColor = null;
      },
      error: () => this.errorMessage.set('No se pudo crear la oficina.'),
    });
  }

  // Edicion inline (nombre + color): reemplaza el viejo window.prompt,
  // que no podia alojar un selector de color.
  entrarEdicion(folder: FolderDto): void {
    this.edicionNombre = folder.name;
    this.edicionColor = folder.color;
    this.editandoId.set(folder.id);
  }

  cancelarEdicion(): void {
    this.editandoId.set(null);
  }

  guardarEdicion(folder: FolderDto): void {
    const nombre = this.edicionNombre.trim();
    if (!nombre) return;

    this.folderService.rename(folder.id, nombre, this.edicionColor).subscribe({
      next: (actualizada) => {
        this.reemplazar(actualizada);
        this.editandoId.set(null);
      },
      error: () => this.errorMessage.set('No se pudo actualizar la oficina.'),
    });
  }

  // Borrar la carpeta NO borra los proyectos: quedan sin carpeta.
  onEliminar(folder: FolderDto): void {
    const ok = window.confirm(
      `¿Eliminar la oficina «${folder.name}»?\n\n` +
        `Sus ${folder.projectCount} proyecto(s) NO se eliminan: quedan sin oficina.`
    );
    if (!ok) return;

    this.folderService.delete(folder.id).subscribe({
      next: () => this.folders.update((list) => list.filter((f) => f.id !== folder.id)),
      error: () => this.errorMessage.set('No se pudo eliminar la oficina.'),
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
          `Oficina compartida con ${resultado.folder.sharedWithFullName}. ` +
            `Se reasignaron ${resultado.reassignedTasks} tarea(s).`
        );
      },
      error: (err) =>
        this.errorMessage.set(
          typeof err?.error === 'string' ? err.error : 'No se pudo compartir la oficina.'
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
      error: () => this.errorMessage.set('No se pudo dejar de compartir la oficina.'),
    });
  }

  private reemplazar(folder: FolderDto): void {
    this.folders.update((list) => list.map((f) => (f.id === folder.id ? folder : f)));
  }
}
