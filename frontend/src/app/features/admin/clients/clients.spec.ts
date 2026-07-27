import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { Clients } from './clients';
import { ClientService } from '../../../core/clients/client.service';

/**
 * Lo que se protege aqui es que el panel NUNCA vuelva a mostrar una contraseña.
 *
 * Durante buena parte de la vida del proyecto, dar de alta un cliente devolvia
 * la contraseña generada y esta pantalla la mostraba en un modal. El super-admin
 * tenia entonces que hacersela llegar al cliente por su cuenta, normalmente por
 * chat, y ese canal era el eslabon mas debil de todo el sistema de credenciales.
 *
 * Ahora la contraseña solo viaja por correo. Reintroducir ese modal reabriria el
 * agujero, y es un cambio que parece inofensivo mientras se escribe.
 */
describe('Clients', () => {
  let create: ReturnType<typeof vi.fn>;
  let resetPassword: ReturnType<typeof vi.fn>;
  let list: ReturnType<typeof vi.fn>;

  const cliente = {
    id: 'c1',
    email: 'nuevo@empresa.com',
    name: 'Empresa Nueva',
    quotaBytes: 1024,
    usedBytes: 0,
    isActive: true,
  };

  function setup() {
    list = vi.fn().mockReturnValue(
      of({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasNextPage: false }),
    );
    create = vi.fn().mockReturnValue(of(cliente));
    resetPassword = vi.fn().mockReturnValue(of(void 0));

    TestBed.configureTestingModule({
      imports: [Clients],
      providers: [
        provideTranslateService({ lang: 'es' }),
        { provide: ClientService, useValue: { list, create, resetPassword } },
      ],
    });

    const fixture = TestBed.createComponent(Clients);
    fixture.detectChanges();

    return { fixture, component: fixture.componentInstance as any };
  }

  function fillCreateForm(component: any) {
    component.createForm.setValue({
      email: 'nuevo@empresa.com',
      name: 'Empresa Nueva',
      quotaMb: 100,
    });
  }

  it('el alta avisa de que las credenciales salieron por correo', () => {
    const { component } = setup();

    fillCreateForm(component);
    component.create();

    expect(create).toHaveBeenCalledOnce();

    // Se confirma el destinatario, no la credencial.
    expect(component.credentialsSentTo()).toBe('nuevo@empresa.com');
  });

  it('el alta no expone ninguna contrasena en el estado del componente', () => {
    const { component } = setup();

    // El servicio devuelve un cliente sin contraseña, pero se simula ademas una
    // respuesta contaminada: aunque el backend la devolviera por error, la
    // pantalla no debe recogerla ni mostrarla.
    create.mockReturnValue(of({ ...cliente, generatedPassword: 'NoDeberiaVerse123' }));

    fillCreateForm(component);
    component.create();

    const estado = JSON.stringify(
      Object.entries(component)
        .filter(([, value]) => typeof value === 'function' && 'set' in (value as object))
        .map(([key, signalFn]) => [key, (signalFn as () => unknown)()]),
    );

    expect(estado).not.toContain('NoDeberiaVerse123');
  });

  it('el reseteo avisa por correo y no devuelve nada que mostrar', () => {
    const { component } = setup();

    component.resetPassword({ ...cliente, email: 'existente@empresa.com' });

    expect(resetPassword).toHaveBeenCalledWith('c1');
    expect(component.credentialsSentTo()).toBe('existente@empresa.com');
  });

  it('el aviso se puede cerrar', () => {
    const { component } = setup();

    fillCreateForm(component);
    component.create();
    expect(component.credentialsSentTo()).not.toBeNull();

    component.dismissCredentialsNotice();
    expect(component.credentialsSentTo()).toBeNull();
  });

  it('un email duplicado se explica como tal', () => {
    const { component } = setup();
    create.mockReturnValue(throwError(() => ({ status: 409 })));

    fillCreateForm(component);
    component.create();

    expect(component.createError()).toBe('clients.errors.emailTaken');
    expect(component.credentialsSentTo()).toBeNull();
  });

  it('no llama al servicio con el formulario incompleto', () => {
    const { component } = setup();

    component.create();

    expect(create).not.toHaveBeenCalled();
  });
});
