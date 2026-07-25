import { HttpErrorResponse, HttpRequest, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { lastValueFrom, of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

/**
 * El interceptor decide que hacer ante un 401. Se prueban las tres ramas que mas
 * importan, incluida la que causo un bug real: un 401 de /change-password (que
 * significa "contraseña actual incorrecta") NO debe intentar refrescar ni
 * desloguear, solo propagar el error.
 */
describe('authInterceptor', () => {
  function setup(auth: Partial<AuthService>, navigate = vi.fn()) {
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: Router, useValue: { navigate } },
      ],
    });
    return { navigate };
  }

  function run(url: string, next: (req: HttpRequest<unknown>) => ReturnType<typeof of>) {
    return TestBed.runInInjectionContext(() =>
      authInterceptor(new HttpRequest('GET', url), next as never),
    );
  }

  const unauthorized = () => throwError(() => new HttpErrorResponse({ status: 401 }));

  it('un 401 en /change-password no refresca ni desloguea, solo propaga', async () => {
    const refresh = vi.fn();
    const clearSession = vi.fn();
    setup({ getAccessToken: () => 'token', refresh: refresh as never, clearSession });

    const next = vi.fn(unauthorized);

    await expect(
      lastValueFrom(run('https://api/me/change-password', next)),
    ).rejects.toBeInstanceOf(HttpErrorResponse);

    expect(refresh).not.toHaveBeenCalled();
    expect(clearSession).not.toHaveBeenCalled();
  });

  it('un 401 normal dispara refresh y reintenta con el token nuevo', async () => {
    const refresh = vi.fn(() => of({ accessToken: 'nuevo-token' }));
    setup({ getAccessToken: () => 'token', refresh: refresh as never, clearSession: vi.fn() });

    const okResponse = new HttpResponse({ status: 200, body: 'ok' });
    const next = vi
      .fn()
      .mockReturnValueOnce(unauthorized())
      .mockReturnValueOnce(of(okResponse));

    const result = await lastValueFrom(run('https://api/files', next));

    expect(refresh).toHaveBeenCalledOnce();
    expect(next).toHaveBeenCalledTimes(2); // original + reintento
    expect(result).toBe(okResponse);
  });

  it('si el refresh falla, limpia la sesion y redirige al login', async () => {
    const clearSession = vi.fn();
    const refresh = vi.fn(() => throwError(() => new Error('refresh fallido')));
    const { navigate } = setup(
      { getAccessToken: () => 'token', refresh: refresh as never, clearSession },
    );

    const next = vi.fn(unauthorized);

    await expect(lastValueFrom(run('https://api/files', next))).rejects.toBeTruthy();

    expect(clearSession).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/login']);
  });
});
