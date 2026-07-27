import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { ForgotPassword } from './forgot-password';
import { AuthService } from '../../core/auth/auth.service';

/**
 * Lo que se protege aqui es una propiedad de seguridad, no una comodidad: la
 * vista muestra el MISMO acuse pase lo que pase, incluso si la peticion falla.
 *
 * El endpoint responde 204 siempre para no revelar que correos estan
 * registrados. Si la vista distinguiera los casos ("no encontramos esa cuenta",
 * o simplemente un mensaje de error tecnico solo cuando el email no existe),
 * devolveria por la interfaz la informacion que la API se guarda, y todo el
 * cuidado del backend no habria servido de nada.
 */
describe('ForgotPassword', () => {
  let forgotPassword: ReturnType<typeof vi.fn>;

  function setup() {
    forgotPassword = vi.fn().mockReturnValue(of(void 0));

    TestBed.configureTestingModule({
      imports: [ForgotPassword],
      providers: [
        provideTranslateService({ lang: 'es' }),
        provideRouter([]),
        { provide: AuthService, useValue: { forgotPassword } },
      ],
    });

    const fixture = TestBed.createComponent(ForgotPassword);
    fixture.detectChanges();

    // Los miembros son protected; el test los alcanza via any para no tener que
    // exponer internals solo para poder probarlos.
    return { fixture, component: fixture.componentInstance as any };
  }

  it('no llama al servicio con el formulario vacio', () => {
    const { component } = setup();

    component.submit();

    expect(forgotPassword).not.toHaveBeenCalled();
  });

  it('no llama al servicio con un email mal formado', () => {
    const { component } = setup();

    component.form.setValue({ email: 'esto-no-es-un-email' });
    component.submit();

    expect(forgotPassword).not.toHaveBeenCalled();
  });

  it('muestra el acuse tras una peticion correcta', () => {
    const { component } = setup();

    component.form.setValue({ email: 'cliente@example.com' });
    component.submit();

    expect(forgotPassword).toHaveBeenCalledWith('cliente@example.com');
    expect(component.isDone()).toBe(true);
  });

  it('muestra EL MISMO acuse aunque la peticion falle', () => {
    const { component } = setup();
    forgotPassword.mockReturnValue(throwError(() => ({ status: 500 })));

    component.form.setValue({ email: 'cliente@example.com' });
    component.submit();

    // Si esto cambiara a mostrar un error, un atacante podria deducir por la
    // interfaz que cuentas existen. El acuse es deliberadamente indistinguible.
    expect(component.isDone()).toBe(true);
  });

  it('deja de estar enviando en ambos desenlaces', () => {
    const { component } = setup();

    component.form.setValue({ email: 'cliente@example.com' });
    component.submit();
    expect(component.isSubmitting()).toBe(false);

    forgotPassword.mockReturnValue(throwError(() => ({ status: 500 })));
    component.submit();
    expect(component.isSubmitting()).toBe(false);
  });
});
