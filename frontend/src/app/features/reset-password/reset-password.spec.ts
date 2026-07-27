import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { ResetPassword } from './reset-password';
import { AuthService } from '../../core/auth/auth.service';

/**
 * La vista que abre el enlace del correo. Tiene mas logica propia que el resto:
 * lee el token de la URL, exige que las dos contraseñas coincidan y traduce el
 * codigo de error a un mensaje util.
 *
 * Distinguir el 401 del 400 importa: son problemas distintos y el usuario no
 * puede resolverlos igual. Con un 401 tiene que pedir otro enlace; con un 400
 * le basta con elegir una contraseña mejor.
 */
describe('ResetPassword', () => {
  let resetPassword: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.fn>;

  function setup(token: string | null = 'un-token-del-correo') {
    resetPassword = vi.fn().mockReturnValue(of(void 0));
    navigate = vi.fn();

    TestBed.configureTestingModule({
      imports: [ResetPassword],
      providers: [
        provideTranslateService({ lang: 'es' }),
        { provide: AuthService, useValue: { resetPassword } },
        { provide: Router, useValue: { navigateByUrl: navigate } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: { get: () => token } } },
        },
      ],
    });

    const fixture = TestBed.createComponent(ResetPassword);
    fixture.detectChanges();

    return { fixture, component: fixture.componentInstance as any };
  }

  function fillForm(component: any, password: string, confirmation = password) {
    component.form.setValue({ password, confirmation });
  }

  it('lee el token de la query string', () => {
    const { component } = setup();
    expect(component.token).toBe('un-token-del-correo');
  });

  it('sin token no llama al servicio', () => {
    const { component } = setup(null);

    fillForm(component, 'ContrasenaNueva2026');
    component.submit();

    // Se llega aqui sin token entrando a mano a /reset-password. La vista lo
    // detecta y explica que hay que abrir el enlace del correo.
    expect(resetPassword).not.toHaveBeenCalled();
  });

  it('no envia si las contrasenas no coinciden', () => {
    const { component } = setup();

    fillForm(component, 'ContrasenaNueva2026', 'OtraDistinta2026');
    component.submit();

    // Escribir mal una contraseña que no se ve, dos veces igual, es facil. Sin
    // esta comprobacion el usuario se quedaria fuera de su propia cuenta.
    expect(resetPassword).not.toHaveBeenCalled();
    expect(component.form.hasError('passwordMismatch')).toBe(true);
  });

  it('no envia una contrasena mas corta que el minimo', () => {
    const { component } = setup();

    // El minimo coincide con el del backend: 12 caracteres.
    fillForm(component, 'corta');
    component.submit();

    expect(resetPassword).not.toHaveBeenCalled();
  });

  it('canjea el token y muestra el final feliz', () => {
    const { component } = setup();

    fillForm(component, 'ContrasenaNueva2026');
    component.submit();

    expect(resetPassword).toHaveBeenCalledWith('un-token-del-correo', 'ContrasenaNueva2026');
    expect(component.isDone()).toBe(true);
    expect(component.errorKey()).toBeNull();
  });

  it('un 401 se explica como enlace vencido o ya usado', () => {
    const { component } = setup();
    resetPassword.mockReturnValue(throwError(() => ({ status: 401 })));

    fillForm(component, 'ContrasenaNueva2026');
    component.submit();

    expect(component.errorKey()).toBe('resetPassword.errors.invalidToken');
    expect(component.isDone()).toBe(false);
  });

  it('un 400 se explica como contrasena que no cumple', () => {
    const { component } = setup();
    resetPassword.mockReturnValue(throwError(() => ({ status: 400 })));

    fillForm(component, 'ContrasenaNueva2026');
    component.submit();

    // Distinto del 401: aqui el enlace sirve, lo que falla es la contraseña.
    expect(component.errorKey()).toBe('resetPassword.errors.weakPassword');
  });

  it('cualquier otro fallo cae en el mensaje generico', () => {
    const { component } = setup();
    resetPassword.mockReturnValue(throwError(() => ({ status: 500 })));

    fillForm(component, 'ContrasenaNueva2026');
    component.submit();

    expect(component.errorKey()).toBe('resetPassword.errors.unexpected');
  });

  it('lleva al login cuando se pide', () => {
    const { component } = setup();

    component.goToLogin();

    expect(navigate).toHaveBeenCalledWith('/login');
  });
});
