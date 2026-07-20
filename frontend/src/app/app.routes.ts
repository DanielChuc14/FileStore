import { Routes } from '@angular/router';

import { authGuard, guestGuard, superAdminGuard } from './core/auth/auth.guards';

/**
 * `withComponentInputBinding` en app.config hace que estos `data` lleguen al
 * componente como inputs, sin que Placeholder tenga que leer la ruta.
 */
const placeholder = (titleKey: string, phase: number) => ({
  loadComponent: () => import('./features/placeholder/placeholder').then((m) => m.Placeholder),
  data: { titleKey, phase },
});

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/login/login').then((m) => m.Login),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell').then((m) => m.Shell),
    children: [
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
      {
        path: 'dashboard',
        loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
      },
      {
        path: 'admin/clients',
        canActivate: [superAdminGuard],
        loadComponent: () =>
          import('./features/admin/clients/clients').then((m) => m.Clients),
      },
      {
        path: 'api-keys',
        loadComponent: () => import('./features/api-keys/api-keys').then((m) => m.ApiKeys),
      },
      {
        path: 'files',
        loadComponent: () => import('./features/files/files').then((m) => m.Files),
      },
      { path: 'trash', ...placeholder('nav.trash', 6) },
      { path: 'audit', ...placeholder('nav.audit', 7) },
      { path: 'profile', ...placeholder('nav.profile', 8) },
    ],
  },
  { path: '**', redirectTo: '' },
];
