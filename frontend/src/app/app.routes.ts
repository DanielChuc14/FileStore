import { Routes } from '@angular/router';

import { authGuard, clientGuard, guestGuard, superAdminGuard } from './core/auth/auth.guards';

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
        canActivate: [clientGuard],
        loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
      },
      {
        path: 'admin/overview',
        canActivate: [superAdminGuard],
        loadComponent: () =>
          import('./features/admin/overview/overview').then((m) => m.AdminOverview),
      },
      {
        path: 'admin/clients',
        canActivate: [superAdminGuard],
        loadComponent: () => import('./features/admin/clients/clients').then((m) => m.Clients),
      },
      {
        path: 'admin/settings',
        canActivate: [superAdminGuard],
        loadComponent: () =>
          import('./features/admin/settings/settings').then((m) => m.AdminSettings),
      },
      {
        path: 'api-keys',
        canActivate: [clientGuard],
        loadComponent: () => import('./features/api-keys/api-keys').then((m) => m.ApiKeys),
      },
      {
        path: 'files',
        canActivate: [clientGuard],
        loadComponent: () => import('./features/files/files').then((m) => m.Files),
      },
      {
        path: 'trash',
        canActivate: [clientGuard],
        loadComponent: () => import('./features/trash/trash').then((m) => m.Trash),
      },
      {
        path: 'audit',
        canActivate: [clientGuard],
        loadComponent: () => import('./features/audit/audit').then((m) => m.Audit),
      },
      { path: 'profile', canActivate: [clientGuard], ...placeholder('nav.profile', 8) },
    ],
  },
  { path: '**', redirectTo: '' },
];
