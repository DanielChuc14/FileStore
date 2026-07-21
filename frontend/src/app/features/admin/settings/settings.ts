import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';

import { StatsService } from '../../../core/stats/stats.service';
import { AllowedType } from '../../../core/stats/stats.models';

@Component({
  selector: 'app-admin-settings',
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './settings.html',
})
export class AdminSettings implements OnInit {
  private readonly service = inject(StatsService);
  private readonly fb = inject(FormBuilder);

  protected readonly types = signal<AllowedType[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly isSaving = signal(false);
  protected readonly savedMessage = signal(false);
  protected readonly errorKey = signal<string | null>(null);

  // El tamaño se edita en MB aunque la API trabaje en bytes: nadie razona
  // en bytes al configurar un limite.
  protected readonly configForm = this.fb.nonNullable.group({
    maxFileSizeMb: [10, [Validators.required, Validators.min(1)]],
    trashRetentionDays: [30, [Validators.required, Validators.min(1), Validators.max(365)]],
    rateLimitDefaultPerMinute: [100, [Validators.required, Validators.min(1)]],
  });

  ngOnInit(): void {
    this.service.getConfig().subscribe({
      next: (config) => {
        this.configForm.patchValue({
          maxFileSizeMb: Math.round(config.maxFileSizeBytes / 1024 / 1024),
          trashRetentionDays: config.trashRetentionDays,
          rateLimitDefaultPerMinute: config.rateLimitDefaultPerMinute,
        });
        this.isLoading.set(false);
      },
      error: () => {
        this.errorKey.set('settings.errors.load');
        this.isLoading.set(false);
      },
    });

    this.loadTypes();
  }

  protected loadTypes(): void {
    this.service.getAllowedTypes().subscribe({
      next: (types) => this.types.set(types),
      error: () => this.errorKey.set('settings.errors.load'),
    });
  }

  protected save(): void {
    if (this.configForm.invalid || this.isSaving()) {
      this.configForm.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.savedMessage.set(false);
    this.errorKey.set(null);

    const { maxFileSizeMb, trashRetentionDays, rateLimitDefaultPerMinute } =
      this.configForm.getRawValue();

    this.service
      .updateConfig({
        maxFileSizeBytes: maxFileSizeMb * 1024 * 1024,
        trashRetentionDays,
        rateLimitDefaultPerMinute,
      })
      .subscribe({
        next: () => {
          this.isSaving.set(false);
          this.savedMessage.set(true);
          setTimeout(() => this.savedMessage.set(false), 3000);
        },
        error: () => {
          this.isSaving.set(false);
          this.errorKey.set('settings.errors.save');
        },
      });
  }

  protected toggleType(type: AllowedType): void {
    this.service.updateAllowedType(type.id, !type.isEnabled).subscribe({
      next: (updated) => {
        this.types.update((list) =>
          list.map((t) => (t.id === updated.id ? updated : t)),
        );
      },
      error: () => this.errorKey.set('settings.errors.save'),
    });
  }
}
