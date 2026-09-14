import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../../core/services/auth.service';
import { UserService } from '../../../core/services/user.service';
import { AreaService, AreaDto } from '../../../core/services/area.service';

@Component({
  selector: 'app-completar-registro',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatSelectModule,
    MatButtonModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './completar-registro.component.html',
  styleUrl: './completar-registro.component.scss',
})
export class CompletarRegistroComponent implements OnInit {
  // Signals en vez de propiedades planas: en una app Zoneless, Angular
  // solo sabe que debe re-renderizar cuando un signal cambia. Mutar
  // "this.loadingAreas = false" en una propiedad normal no dispara
  // deteccion de cambios sin Zone.js corriendo detras.
  areas = signal<AreaDto[]>([]);
  loadingAreas = signal(true);
  submitting = signal(false);
  errorMessage = signal<string | null>(null);
  fullName = signal('');
  email = signal('');

  form!: ReturnType<FormBuilder['group']>;

  constructor(
    private fb: FormBuilder,
    private auth: AuthService,
    private userService: UserService,
    private areaService: AreaService,
    private router: Router
  ) {
    this.fullName.set(this.auth.getFullName() ?? '');
    this.email.set(this.auth.getEmail() ?? '');
    this.form = this.fb.group({
      areaId: [null as number | null, Validators.required],
    });
  }

  ngOnInit(): void {
    this.areaService.getAll().subscribe({
      next: (areas) => {
        this.areas.set(areas);
        this.loadingAreas.set(false);
      },
      error: () => {
        this.errorMessage.set('No se pudo cargar el catálogo de áreas. Intenta recargar la página.');
        this.loadingAreas.set(false);
      },
    });
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const userId = this.auth.getUserId();
    const username = this.auth.getUsername();

    if (!userId || !username) {
      this.errorMessage.set('No se pudo leer tu identidad desde la sesión. Intenta iniciar sesión de nuevo.');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    this.userService
      .register({
        id: userId,
        username,
        email: this.email(),
        fullName: this.fullName(),
        areaId: this.form.value.areaId!,
      })
      .subscribe({
        next: () => {
          this.router.navigate(['/proyectos']);
        },
        error: (err) => {
          this.submitting.set(false);
          this.errorMessage.set(
            err.status === 409
              ? 'Tu usuario ya estaba registrado. Redirigiendo...'
              : 'Ocurrió un error al completar tu registro. Intenta de nuevo.'
          );

          if (err.status === 409) {
            setTimeout(() => this.router.navigate(['/proyectos']), 1500);
          }
        },
      });
  }
}