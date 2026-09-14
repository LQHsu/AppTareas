import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';
 
// Adjunta "Authorization: Bearer <token>" a toda peticion que vaya
// hacia nuestra API. Las peticiones a otros dominios (ej. assets
// externos) no se tocan.
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.getToken();
 
  if (!token || !req.url.includes('/api/')) {
    return next(req);
  }
 
  const authReq = req.clone({
    setHeaders: { Authorization: `Bearer ${token}` },
  });
 
  return next(authReq);
};
 