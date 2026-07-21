import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { Login } from './login';
import { AuthService } from '../../core/auth/auth.service';

/**
 * Test de componente: verifica el COMPORTAMIENTO de la pantalla de login sin
 * un backend real. El AuthService se reemplaza por un doble que se puede
 * programar para responder exito o error, y se comprueba que el componente
 * reacciona bien a cada caso.
 */
describe('Login', () => {
  let authLogin: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.fn>;

  function setup() {
    authLogin = vi.fn();
    navigate = vi.fn();

    TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideTranslateService({ lang: 'es' }),
        { provide: AuthService, useValue: { login: authLogin } },
        { provide: Router, useValue: { navigateByUrl: navigate } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: { get: () => null } } },
        },
      ],
    });

    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();
    // Los miembros son protected; el test los alcanza via any, que es aceptable
    // en pruebas para no tener que exponer internals solo para testear.
    const component = fixture.componentInstance as any;
    return { fixture, component };
  }

  it('se crea', () => {
    const { component } = setup();
    expect(component).toBeTruthy();
  });

  it('no llama a login con el formulario vacio', () => {
    const { component } = setup();

    component.submit();

    // Sin email ni password el formulario es invalido: no debe llegar al servicio.
    expect(authLogin).not.toHaveBeenCalled();
  });

  it('muestra error de credenciales ante un 401', () => {
    const { component } = setup();
    authLogin.mockReturnValue(throwError(() => ({ status: 401 })));

    component.form.setValue({ email: 'cliente@example.com', password: 'incorrecta' });
    component.submit();

    expect(authLogin).toHaveBeenCalledOnce();
    expect(component.errorKey()).toBe('login.errors.invalidCredentials');
  });

  it('muestra error tecnico ante un fallo que no es 401', () => {
    const { component } = setup();
    authLogin.mockReturnValue(throwError(() => ({ status: 500 })));

    component.form.setValue({ email: 'cliente@example.com', password: 'loquesea' });
    component.submit();

    expect(component.errorKey()).toBe('login.errors.unexpected');
  });

  it('redirige al dashboard tras un login de cliente', () => {
    const { component } = setup();
    authLogin.mockReturnValue(of({ role: 'Client', email: 'c@example.com', userId: '1' }));

    component.form.setValue({ email: 'cliente@example.com', password: 'correcta12345' });
    component.submit();

    expect(navigate).toHaveBeenCalledWith('/dashboard');
  });

  it('redirige al panel admin tras un login de super-admin', () => {
    const { component } = setup();
    authLogin.mockReturnValue(of({ role: 'SuperAdmin', email: 'a@example.com', userId: '1' }));

    component.form.setValue({ email: 'admin@example.com', password: 'correcta12345' });
    component.submit();

    expect(navigate).toHaveBeenCalledWith('/admin/overview');
  });
});
