import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

// Protege rutas que requieren sesion iniciada. auth.init() ya corrio
// como APP_INITIALIZER antes de que el router resuelva cualquier ruta
// (ver app.config.ts), asi que isAuthenticated() ya refleja el estado
// real de la sesion para cuando este guard se evalua.
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/login']);
};
