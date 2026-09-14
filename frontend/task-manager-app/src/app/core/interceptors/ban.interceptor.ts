import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

// Detecta el 403 con { error: "account_banned" } que manda
// BanCheckMiddleware (backend) para CUALQUIER endpoint cuando el usuario
// esta bloqueado, y redirige a una pantalla dedicada en vez de dejar que
// cada componente muestre su mensaje de error generico de siempre.
//
// UserService.getMe() atrapa sus propios errores y siempre resuelve a
// null (lo necesita: "no soy super admin" y "no estoy registrado
// todavia" tambien pasan por ahi). Eso significa que quien llamo getMe()
// puede reaccionar al null con su propia navegacion (ej. login.component
// mandando a /completar-registro) en el mismo tick sincrono en que este
// interceptor ya disparo la suya hacia /cuenta-bloqueada. Angular Router
// cancela la navegacion en curso cuando llega una nueva, asi que la
// segunda gana — por eso la redireccion de aca se difiere a un
// microtask: se ejecuta despues de que termine ese flujo sincrono, para
// ser siempre la ultima y quedarse ganando la carrera.
export const banInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);

  return next(req).pipe(
    catchError((err: unknown) => {
      if (
        err instanceof HttpErrorResponse &&
        err.status === 403 &&
        err.error?.error === 'account_banned' &&
        router.url !== '/cuenta-bloqueada'
      ) {
        queueMicrotask(() => router.navigateByUrl('/cuenta-bloqueada'));
      }

      return throwError(() => err);
    })
  );
};
