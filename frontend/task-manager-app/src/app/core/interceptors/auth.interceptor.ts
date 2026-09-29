import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { from, switchMap } from 'rxjs';
import { AuthService } from '../services/auth.service';

// Adjunta "Authorization: Bearer <token>" a toda peticion que vaya
// hacia nuestra API. Las peticiones a otros dominios (ej. assets
// externos) no se tocan.
//
// auth.getToken() es async porque de paso refresca el token si esta a
// punto de expirar (ver comentario ahi) - sin eso, cualquier sesion de
// mas de 5 minutos empezaba a mandar un JWT vencido y todo se caia con
// 401 hasta que la persona hacia F5.
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);

  if (!req.url.includes('/api/')) {
    return next(req);
  }

  return from(auth.getToken()).pipe(
    switchMap((token) => {
      if (!token) return next(req);

      const authReq = req.clone({
        setHeaders: { Authorization: `Bearer ${token}` },
      });
      return next(authReq);
    })
  );
};
