import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { AuthService } from './auth.service';
import { environment } from '../../../environments/environment';

/**
 * Test de servicio: no hay backend, se intercepta la peticion HTTP y se
 * comprueba a donde va, que lleva y que hace el servicio con la respuesta.
 *
 * Lo mas importante que se fija aqui es que el access token viva SOLO en
 * memoria. Es una decision de seguridad deliberada (un XSS no tendria de donde
 * leerlo) que un refactor bienintencionado podria deshacer "para que la sesion
 * sobreviva al recargar", sin darse cuenta de lo que rompe.
 */
describe('AuthService', () => {
  const base = `${environment.apiBaseUrl}/auth`;

  let service: AuthService;
  let http: HttpTestingController;

  const sesion = {
    accessToken: 'un-token',
    expiresAt: '2026-01-01T00:00:00Z',
    userId: 'c1',
    email: 'cliente@example.com',
    role: 'Client',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('el login guarda la sesion en memoria', () => {
    let user: unknown;
    service.login({ email: 'cliente@example.com', password: 'secreta' }).subscribe((u) => (user = u));

    const req = http.expectOne(`${base}/login`);
    expect(req.request.method).toBe('POST');

    // Sin withCredentials el navegador no acepta la cookie de refresh.
    expect(req.request.withCredentials).toBe(true);

    req.flush(sesion);

    expect(service.getAccessToken()).toBe('un-token');
    expect(service.isAuthenticated()).toBe(true);
    expect(user).toEqual({ userId: 'c1', email: 'cliente@example.com', role: 'Client' });
  });

  it('el access token no se persiste en el navegador', () => {
    service.login({ email: 'cliente@example.com', password: 'secreta' }).subscribe();
    http.expectOne(`${base}/login`).flush(sesion);

    // Si alguien "mejora" esto guardandolo, un XSS pasa de molesto a fatal.
    expect(localStorage.getItem('accessToken')).toBeNull();
    expect(JSON.stringify(localStorage)).not.toContain('un-token');
    expect(JSON.stringify(sessionStorage)).not.toContain('un-token');
  });

  it('reconoce al super-admin por su rol', () => {
    service.login({ email: 'admin@example.com', password: 'secreta' }).subscribe();
    http.expectOne(`${base}/login`).flush({ ...sesion, role: 'SuperAdmin' });

    expect(service.isSuperAdmin()).toBe(true);
  });

  it('restoreSession devuelve false y limpia si no hay cookie', () => {
    let restored: boolean | undefined;
    service.restoreSession().subscribe((r) => (restored = r));

    http.expectOne(`${base}/refresh`).flush(null, { status: 401, statusText: 'Unauthorized' });

    // Arrancar la app sin sesion es lo normal, no un error que deba propagarse.
    expect(restored).toBe(false);
    expect(service.isAuthenticated()).toBe(false);
  });

  it('logout limpia la sesion aunque el servidor falle', () => {
    service.login({ email: 'cliente@example.com', password: 'secreta' }).subscribe();
    http.expectOne(`${base}/login`).flush(sesion);

    service.logout().subscribe();
    http.expectOne(`${base}/logout`).flush(null, { status: 500, statusText: 'Server Error' });

    // Dejar al usuario "dentro" porque el servidor fallo seria lo peor de los
    // dos mundos: cree que cerro sesion y no la cerro.
    expect(service.isAuthenticated()).toBe(false);
    expect(service.user()).toBeNull();
  });

  it('forgotPassword manda el email al endpoint correcto', () => {
    service.forgotPassword('cliente@example.com').subscribe();

    const req = http.expectOne(`${base}/forgot-password`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'cliente@example.com' });

    req.flush(null);
  });

  it('resetPassword manda el token y la contrasena nueva', () => {
    service.resetPassword('un-token-del-correo', 'ContrasenaNueva2026').subscribe();

    const req = http.expectOne(`${base}/reset-password`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      token: 'un-token-del-correo',
      newPassword: 'ContrasenaNueva2026',
    });

    req.flush(null);
  });
});
