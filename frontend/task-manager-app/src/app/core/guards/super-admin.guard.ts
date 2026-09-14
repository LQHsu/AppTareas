import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { UserService } from '../services/user.service';

// Protege /admin. A diferencia de authGuard (que solo lee un signal ya
// resuelto por el APP_INITIALIZER), aqui puede hacer falta pedir
// GET /api/users/me si nadie lo pidio todavia en esta sesion -por
// ejemplo, si el usuario entra directo a /admin por URL sin pasar antes
// por /proyectos-, asi que el guard resuelve de forma asincrona.
export const superAdminGuard: CanActivateFn = () => {
  const userService = inject(UserService);
  const router = inject(Router);

  const cached = userService.currentUser();
  if (cached) {
    return cached.isSuperAdmin ? true : router.createUrlTree(['/proyectos']);
  }

  return userService.getMe().pipe(
    map((user) => (user?.isSuperAdmin ? true : router.createUrlTree(['/proyectos'])))
  );
};
