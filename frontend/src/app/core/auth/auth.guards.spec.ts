import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';

import { clientGuard, superAdminGuard, guestGuard } from './auth.guards';
import { AuthService } from './auth.service';

/**
 * Los guards deciden quien entra a cada ruta. Se prueban de forma aislada: se
 * inyectan dobles de AuthService y Router, y se verifica que devuelven `true`
 * (deja pasar) o el UrlTree de redireccion correcto. createUrlTree se reemplaza
 * por un doble que devuelve los comandos, para poder aseverar a donde redirige.
 */
describe('auth guards', () => {
  let createUrlTree: ReturnType<typeof vi.fn>;

  function setup(auth: Partial<AuthService>) {
    createUrlTree = vi.fn((commands: unknown[]) => ({ commands }) as unknown);

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: Router, useValue: { createUrlTree } },
      ],
    });
  }

  function run(guard: typeof clientGuard) {
    return TestBed.runInInjectionContext(() =>
      guard({} as never, { url: '/target' } as never),
    );
  }

  describe('clientGuard', () => {
    it('redirige a /login si no esta autenticado', () => {
      setup({ isAuthenticated: (() => false) as never });

      run(clientGuard);

      expect(createUrlTree).toHaveBeenCalledWith(['/login'], {
        queryParams: { returnUrl: '/target' },
      });
    });

    it('redirige al panel de admin si es super-admin', () => {
      setup({ isAuthenticated: (() => true) as never, isSuperAdmin: (() => true) as never });

      run(clientGuard);

      expect(createUrlTree).toHaveBeenCalledWith(['/admin/overview']);
    });

    it('deja pasar a un cliente autenticado', () => {
      setup({ isAuthenticated: (() => true) as never, isSuperAdmin: (() => false) as never });

      expect(run(clientGuard)).toBe(true);
    });
  });

  describe('superAdminGuard', () => {
    it('deja pasar a un super-admin', () => {
      setup({ isAuthenticated: (() => true) as never, isSuperAdmin: (() => true) as never });

      expect(run(superAdminGuard)).toBe(true);
    });

    it('redirige al dashboard si es un cliente', () => {
      setup({ isAuthenticated: (() => true) as never, isSuperAdmin: (() => false) as never });

      run(superAdminGuard);

      expect(createUrlTree).toHaveBeenCalledWith(['/dashboard']);
    });
  });

  describe('guestGuard', () => {
    it('redirige al dashboard si ya esta autenticado', () => {
      setup({ isAuthenticated: (() => true) as never });

      run(guestGuard);

      expect(createUrlTree).toHaveBeenCalledWith(['/dashboard']);
    });

    it('deja ver el login si no esta autenticado', () => {
      setup({ isAuthenticated: (() => false) as never });

      expect(run(guestGuard)).toBe(true);
    });
  });
});
