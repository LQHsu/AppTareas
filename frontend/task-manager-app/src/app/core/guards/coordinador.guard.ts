import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { UserService } from '../services/user.service';

// Protege /altas. Mismo patron que superAdminGuard: un super admin
// tambien puede entrar (le sirve para dar altas en su propia area sin
// necesitar el flag aparte), ver comentario en CoordinadorController.
export const coordinadorGuard: CanActivateFn = () => {
  const userService = inject(UserService);
  const router = inject(Router);

  const puedeEntrar = (user: { isCoordinador: boolean; isSuperAdmin: boolean } | null) =>
    !!user && (user.isCoordinador || user.isSuperAdmin);

  const cached = userService.currentUser();
  if (cached) {
    return puedeEntrar(cached) ? true : router.createUrlTree(['/proyectos']);
  }

  return userService.getMe().pipe(
    map((user) => (puedeEntrar(user) ? true : router.createUrlTree(['/proyectos'])))
  );
};
