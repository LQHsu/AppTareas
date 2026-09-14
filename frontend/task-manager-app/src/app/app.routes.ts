import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { superAdminGuard } from './core/guards/super-admin.guard';

// Todas las pantallas se cargan con loadComponent (lazy). Antes se
// importaban de forma estatica y todo terminaba en el bundle inicial:
// con el crecimiento de la app (editor de texto, estadisticas,
// carpetas...) eso paso de 1 MB y rompio el presupuesto de build.
// Ahora cada pantalla es su propio chunk y solo baja al visitarla.
export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./features/auth/login/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'cuenta-bloqueada',
    loadComponent: () =>
      import('./features/auth/cuenta-bloqueada/cuenta-bloqueada.component').then(
        (m) => m.CuentaBloqueadaComponent
      ),
  },
  {
    path: 'completar-registro',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/auth/completar-registro/completar-registro.component').then(
        (m) => m.CompletarRegistroComponent
      ),
  },
  // Todas las pantallas "de la app" (con el menu lateral persistente)
  // viven bajo este shell. completar-registro queda fuera a proposito:
  // el usuario todavia no tiene area asignada, no tiene sentido
  // mostrarle Proyectos/Mis tareas antes de terminar el registro.
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./shared/components/app-shell/app-shell.component').then((m) => m.AppShellComponent),
    children: [
      {
        path: 'proyectos',
        loadComponent: () =>
          import('./features/proyectos/proyectos.component').then((m) => m.ProyectosComponent),
      },
      {
        path: 'proyectos/:id',
        loadComponent: () =>
          import('./features/proyectos/proyecto-detalle/proyecto-detalle.component').then(
            (m) => m.ProyectoDetalleComponent
          ),
      },
      {
        path: 'carpetas',
        loadComponent: () =>
          import('./features/carpetas/carpetas.component').then((m) => m.CarpetasComponent),
      },
      {
        path: 'mis-tareas',
        loadComponent: () =>
          import('./features/mis-tareas/mis-tareas.component').then((m) => m.MisTareasComponent),
      },
      {
        path: 'admin',
        canActivate: [superAdminGuard],
        loadComponent: () =>
          import('./features/admin/admin.component').then((m) => m.AdminComponent),
      },
    ],
  },
  { path: '', redirectTo: 'login', pathMatch: 'full' },
];
