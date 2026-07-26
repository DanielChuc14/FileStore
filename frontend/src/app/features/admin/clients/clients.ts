import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';

import { ClientService } from '../../../core/clients/client.service';
import { Client } from '../../../core/clients/client.models';
import { FormatBytesPipe } from '../../../shared/format-bytes.pipe';

@Component({
  selector: 'app-clients',
  imports: [ReactiveFormsModule, TranslatePipe, FormatBytesPipe],
  templateUrl: './clients.html',
})
export class Clients implements OnInit {
  private readonly service = inject(ClientService);
  private readonly fb = inject(FormBuilder);

  protected readonly clients = signal<Client[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly isLoading = signal(false);
  protected readonly search = signal('');

  protected readonly showCreateModal = signal(false);
  protected readonly isSaving = signal(false);
  protected readonly createError = signal<string | null>(null);

  /**
   * Correo al que se acaban de enviar credenciales. Ya no se muestra ninguna
   * contraseña en el panel: solo se confirma que salio el correo.
   */
  protected readonly credentialsSentTo = signal<string | null>(null);

  /**
   * Cliente pendiente de baja. Se usa un modal propio y no confirm() nativo:
   * la accion es destructiva y conviene que el dialogo diga que implica, no
   * solo "¿estas seguro?".
   */
  protected readonly pendingRemoval = signal<Client | null>(null);

  protected readonly pageSize = 20;

  protected readonly createForm = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    name: ['', [Validators.required]],
    quotaMb: [100, [Validators.required, Validators.min(1)]],
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.service.list(this.search(), this.page(), this.pageSize).subscribe({
      next: (result) => {
        this.clients.set(result.items);
        this.totalCount.set(result.totalCount);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false),
    });
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    this.page.set(1);
    this.load();
  }

  protected openCreate(): void {
    this.createForm.reset({ email: '', name: '', quotaMb: 100 });
    this.createError.set(null);
    this.showCreateModal.set(true);
  }

  protected create(): void {
    if (this.createForm.invalid || this.isSaving()) {
      this.createForm.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.createError.set(null);

    const { email, name, quotaMb } = this.createForm.getRawValue();

    this.service.create({ email, name, quotaBytes: quotaMb * 1024 * 1024 }).subscribe({
      next: (client) => {
        this.isSaving.set(false);
        this.showCreateModal.set(false);
        this.credentialsSentTo.set(client.email);
        this.load();
      },
      error: (error: { status?: number }) => {
        this.isSaving.set(false);
        this.createError.set(
          error.status === 409 ? 'clients.errors.emailTaken' : 'clients.errors.unexpected',
        );
      },
    });
  }

  protected toggleActive(client: Client): void {
    this.service.update(client.id, { isActive: !client.isActive }).subscribe(() => this.load());
  }

  protected resetPassword(client: Client): void {
    this.service.resetPassword(client.id).subscribe(() => {
      this.credentialsSentTo.set(client.email);
    });
  }

  protected askRemove(client: Client): void {
    this.pendingRemoval.set(client);
  }

  protected confirmRemove(): void {
    const client = this.pendingRemoval();
    if (!client || this.isSaving()) {
      return;
    }

    this.isSaving.set(true);
    this.service.delete(client.id).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.pendingRemoval.set(null);
        this.load();
      },
      error: () => {
        this.isSaving.set(false);
        this.pendingRemoval.set(null);
      },
    });
  }

  protected dismissCredentialsNotice(): void {
    this.credentialsSentTo.set(null);
  }
}
