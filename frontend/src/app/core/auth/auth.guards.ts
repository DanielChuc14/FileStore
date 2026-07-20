import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }

  // returnUrl para volver a donde queria ir despues de autenticarse.
  return router.createUrlTree(['/login'], {
    queryParams: { returnUrl: state.url },
  });
};

export const superAdminGuard: CanActivateFn = (route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const authenticated = authGuard(route, state);
  if (authenticated !== true) {
    return authenticated;
  }

  return auth.isSuperAdmin() ? true : router.createUrlTree(['/dashboard']);
};

/** Evita que un usuario ya autenticado vuelva a la pantalla de login. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.isAuthenticated() ? router.createUrlTree(['/dashboard']) : true;
};
