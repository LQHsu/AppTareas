import { Component, OnInit, input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTableModule } from '@angular/material/table';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import {
  ProjectAttachmentDto,
  TaskAttachmentService,
  formatFileSize,
  saveBlobAsFile,
} from '../../../core/services/task-attachment.service';

// Pestaña "Archivos" del detalle de proyecto: lista plana de todos los
// adjuntos de todas las tareas del proyecto, con la tarea de origen.
// La descarga reusa el endpoint por tarea (el DTO trae taskId).
@Component({
  selector: 'app-proyecto-archivos',
  standalone: true,
  imports: [CommonModule, MatTableModule, MatIconModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './proyecto-archivos.component.html',
  styleUrl: './proyecto-archivos.component.scss',
})
export class ProyectoArchivosComponent implements OnInit {
  projectId = input.required<string>();

  archivos = signal<ProjectAttachmentDto[]>([]);
  loading = signal(true);
  errorMessage = signal<string | null>(null);

  columnas = ['fileName', 'size', 'task', 'uploadedBy', 'acciones'];

  formatFileSize = formatFileSize;

  constructor(private attachmentService: TaskAttachmentService) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.attachmentService.getByProject(this.projectId()).subscribe({
      next: (archivos) => {
        this.archivos.set(archivos);
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('No se pudieron cargar los archivos del proyecto.');
        this.loading.set(false);
      },
    });
  }

  onDownload(archivo: ProjectAttachmentDto): void {
    this.attachmentService.download(archivo.taskId, archivo.id).subscribe({
      next: (blob) => saveBlobAsFile(blob, archivo.fileName),
      error: () => {
        this.errorMessage.set('No se pudo descargar el archivo.');
      },
    });
  }
}
