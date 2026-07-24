import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, catchError, filter, switchMap, take, throwError } from 'rxjs';

import { AuthService } from './auth.service';

/**
 * Estado compartido del refresh. Si tres peticiones reciben 401 a la vez, solo
 * la primera llama a /auth/refresh; las otras dos esperan el token nuevo. Sin
 * esto se dispararian tres refresh en paralelo y, por la rotacion del backend,
 * dos de ellos serian tratados como reutilizacion y cerrarian la sesion.
 */
let isRefreshing = false;
const refreshedToken$ = new BehaviorSubject<string | null>(null);

function withToken(request: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return request.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  // Los endpoints de auth se dejan pasar: no llevan bearer y un 401 ahi es
  // definitivo, no algo que se arregle refrescando.
  if (request.url.includes('/auth/')) {
    return next(request);
  }

  const token = auth.getAccessToken();
  const authorized = token ? withToken(request, token) : request;

  // Endpoints donde un 401 significa "credenciales incorrectas", no "sesion
  // vencida": el cambio de contraseña valida la contraseña ACTUAL, y si es
  // erronea responde 401. Sin esto, el interceptor intentaria refrescar, la
  // peticion volveria a dar 401 y terminaria deslogueando al usuario.
  const isCredentialCheck = request.url.includes('/change-password');

  return next(authorized).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status !== 401 || !token || isCredentialCheck) {
        return throwError(() => error);
      }

      if (isRefreshing) {
        // Esperar a que termine el refresh en curso y reintentar con el token nuevo.
        return refreshedToken$.pipe(
          filter((value): value is string => value !== null),
          take(1),
          switchMap((fresh) => next(withToken(request, fresh))),
        );
      }

      isRefreshing = true;
      refreshedToken$.next(null);

      return auth.refresh().pipe(
        switchMap((response) => {
          isRefreshing = false;
          refreshedToken$.next(response.accessToken);
          return next(withToken(request, response.accessToken));
        }),
        catchError((refreshError) => {
          isRefreshing = false;
          auth.clearSession();
          void router.navigate(['/login']);
          return throwError(() => refreshError);
        }),
      );
    }),
  );
};
