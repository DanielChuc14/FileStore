import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';

import { ApiKeyService } from '../../core/api-keys/api-key.service';
import { ApiKey } from '../../core/api-keys/api-key.models';

@Component({
  selector: 'app-api-keys',
  imports: [ReactiveFormsModule, TranslatePipe, DatePipe],
  templateUrl: './api-keys.html',
})
export class ApiKeys implements OnInit {
  private readonly service = inject(ApiKeyService);
  private readonly fb = inject(FormBuilder);

  protected readonly keys = signal<ApiKey[]>([]);
  protected readonly isLoading = signal(false);
  protected readonly isSaving = signal(false);
  protected readonly showCreateModal = signal(false);

  /** Valor completo recien emitido. Se muestra una vez y no se puede recuperar. */
  protected readonly revealedValue = signal<string | null>(null);
  protected readonly pendingRevoke = signal<ApiKey | null>(null);
  protected readonly pendingRotate = signal<ApiKey | null>(null);

  protected readonly createForm = this.fb.nonNullable.group({
    name: ['', [Validators.required]],
    rateLimitPerMinute: [100, [Validators.min(1)]],
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.service.list().subscribe({
      next: (keys) => {
        this.keys.set(keys);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false),
    });
  }

  protected openCreate(): void {
    this.createForm.reset({ name: '', rateLimitPerMinute: 100 });
    this.showCreateModal.set(true);
  }

  protected create(): void {
    if (this.createForm.invalid || this.isSaving()) {
      this.createForm.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    const { name, rateLimitPerMinute } = this.createForm.getRawValue();

    this.service.create({ name, rateLimitPerMinute }).subscribe({
      next: (result) => {
        this.isSaving.set(false);
        this.showCreateModal.set(false);
        this.revealedValue.set(result.value);
        this.load();
      },
      error: () => this.isSaving.set(false),
    });
  }

  protected confirmRotate(): void {
    const key = this.pendingRotate();
    if (!key) {
      return;
    }

    this.service.rotate(key.id).subscribe((result) => {
      this.pendingRotate.set(null);
      this.revealedValue.set(result.value);
      this.load();
    });
  }

  protected confirmRevoke(): void {
    const key = this.pendingRevoke();
    if (!key) {
      return;
    }

    this.service.revoke(key.id).subscribe(() => {
      this.pendingRevoke.set(null);
      this.load();
    });
  }

  protected copyValue(): void {
    const value = this.revealedValue();
    if (value) {
      void navigator.clipboard.writeText(value);
    }
  }
}
