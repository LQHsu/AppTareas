import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatTableModule } from '@angular/material/table';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CoordinadorService, AltaCoordinadorDto, AltaPreviewDto } from '../../core/services/coordinador.service';
import { FolderService, FolderDto } from '../../core/services/folder.service';

// Pantalla de coordinador: dar de alta numeros economicos autorizados a
// entrar a la app (ver README/CoordinadorController). Especifico del
// proceso institucional de UAMX.
@Component({
  selector: 'app-altas',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatTooltipModule,
    MatTableModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './altas.component.html',
  styleUrl: './altas.component.scss',
})
export class AltasComponent implements OnInit {
  altas = signal<AltaCoordinadorDto[]>([]);
  loading = signal(true);
  submitting = signal(false);
  deletingId = signal<string | null>(null);
  updatingOficinaId = signal<string | null>(null);
  refreshingId = signal<string | null>(null);
  errorMessage = signal<string | null>(null);

  preview = signal<AltaPreviewDto | null>(null);
  previewLoading = signal(false);
  previewError = signal<string | null>(null);

  // Oficinas propias para elegir a cual asignar la nueva alta desde ya
  // (ver AltaCoordinador.OficinaId) - las mismas que se administran en
  // /carpetas, solo las que uno es dueno (no las que le comparten).
  oficinas = signal<FolderDto[]>([]);

  columns = [
    'numeroEconomico',
    'nombreCompleto',
    'correoInstitucional',
    'areaInstitucional',
    'oficina',
    'estado',
    'createdByNombre',
    'createdAt',
    'acciones',
  ];

  form!: ReturnType<FormBuilder['group']>;

  constructor(
    private fb: FormBuilder,
    private coordinadorService: CoordinadorService,
    private folderService: FolderService
  ) {
    this.form = this.fb.group({
      numeroEconomico: ['', [Validators.required, Validators.maxLength(20)]],
      oficinaId: [null as string | null],
    });
  }

  ngOnInit(): void {
    this.loadAltas();
    this.loadOficinas();

    // Cualquier cambio al numero economico invalida el preview que se
    // haya mostrado antes (evita confirmar viendo los datos de otro
    // numero distinto al que quedo en el input).
    this.form.get('numeroEconomico')?.valueChanges.subscribe(() => {
      this.preview.set(null);
      this.previewError.set(null);
    });
  }

  buscar(): void {
    const numeroEconomico = this.form.value.numeroEconomico?.trim();
    if (!numeroEconomico) {
      this.form.markAllAsTouched();
      return;
    }

    this.previewLoading.set(true);
    this.previewError.set(null);
    this.preview.set(null);

    this.coordinadorService.previewAlta(numeroEconomico).subscribe({
      next: (preview) => {
        this.preview.set(preview);
        this.previewLoading.set(false);
      },
      error: () => {
        this.previewLoading.set(false);
        this.previewError.set('No se pudo consultar la información institucional. Puedes dar de alta de todas formas.');
      },
    });
  }

  loadAltas(): void {
    this.loading.set(true);
    this.coordinadorService.getAltas().subscribe({
      next: (altas) => {
        this.altas.set(altas);
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('No se pudo cargar la lista de altas.');
        this.loading.set(false);
      },
    });
  }

  loadOficinas(): void {
    this.folderService.getMine().subscribe({
      next: (folders) => this.oficinas.set(folders.filter((f) => f.isOwner)),
      error: () => this.errorMessage.set('No se pudieron cargar las oficinas.'),
    });
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const numeroEconomico = this.form.value.numeroEconomico!.trim();
    const oficinaId = this.form.value.oficinaId ?? null;
    this.submitting.set(true);
    this.errorMessage.set(null);

    this.coordinadorService.crearAlta(numeroEconomico, oficinaId).subscribe({
      next: (alta) => {
        this.altas.update((list) => [alta, ...list]);
        this.form.reset();
        this.submitting.set(false);
      },
      error: (err) => {
        this.submitting.set(false);
        this.errorMessage.set(
          err?.status === 409
            ? 'Ese número económico ya está dado de alta.'
            : 'No se pudo dar de alta a esa persona.'
        );
      },
    });
  }

  // Cambiar la oficina directo desde la tabla, sin pasar por el
  // formulario de alta. Solo aplica mientras la alta sigue pendiente
  // (el backend lo rechaza si ya se activo - ver comentario en
  // CoordinadorController.UpdateOficina).
  cambiarOficina(alta: AltaCoordinadorDto, oficinaId: string | null): void {
    this.updatingOficinaId.set(alta.numeroEconomico);
    this.coordinadorService.actualizarOficina(alta.numeroEconomico, oficinaId).subscribe({
      next: (actualizada) => {
        this.altas.update((list) =>
          list.map((a) => (a.numeroEconomico === actualizada.numeroEconomico ? actualizada : a))
        );
        this.updatingOficinaId.set(null);
      },
      error: () => {
        this.updatingOficinaId.set(null);
        this.errorMessage.set('No se pudo cambiar la oficina de esa persona.');
      },
    });
  }

  // Vuelve a consultar CUSXACDI/info_usuarios_unidad para esa persona.
  // A diferencia de cambiarOficina/eliminar, esto SI funciona con altas
  // ya activadas: esta pantalla tambien sirve para monitorear el
  // estado institucional de gente que ya entro a la app, no solo para
  // dar altas nuevas.
  refrescar(alta: AltaCoordinadorDto): void {
    this.refreshingId.set(alta.numeroEconomico);
    this.coordinadorService.refrescarDatos(alta.numeroEconomico).subscribe({
      next: (actualizada) => {
        this.altas.update((list) =>
          list.map((a) => (a.numeroEconomico === actualizada.numeroEconomico ? actualizada : a))
        );
        this.refreshingId.set(null);
      },
      error: () => {
        this.refreshingId.set(null);
        this.errorMessage.set('No se pudo actualizar la información institucional de esa persona.');
      },
    });
  }

  eliminar(alta: AltaCoordinadorDto): void {
    this.deletingId.set(alta.numeroEconomico);
    this.coordinadorService.eliminarAlta(alta.numeroEconomico).subscribe({
      next: () => {
        this.altas.update((list) => list.filter((a) => a.numeroEconomico !== alta.numeroEconomico));
        this.deletingId.set(null);
      },
      error: () => {
        this.deletingId.set(null);
        this.errorMessage.set('No se pudo quitar esa alta.');
      },
    });
  }
}
