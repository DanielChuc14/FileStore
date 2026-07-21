import { Component, OnInit, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';

import { environment } from '../../../environments/environment';
import { FormatBytesPipe } from '../../shared/format-bytes.pipe';

interface Profile {
  id: string;
  email: string;
  name: string;
  quotaBytes: number;
  usedBytes: number;
  trashRetentionDays: number | null;
  maxFileSizeBytes: number | null;
  createdAt: string;
}

@Component({
  selector: 'app-profile',
  imports: [ReactiveFormsModule, TranslatePipe, DatePipe, FormatBytesPipe],
  templateUrl: './profile.html',
})
export class ProfileView implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly fb = inject(FormBuilder);

  protected readonly profile = signal<Profile | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly isSaving = signal(false);
  protected readonly successKey = signal<string | null>(null);
  protected readonly errorKey = signal<string | null>(null);

  protected readonly passwordForm = this.fb.nonNullable.group({
    currentPassword: ['', [Validators.required]],
    // El minimo coincide con el del backend: si difirieran, el formulario
    // dejaria enviar algo que el servidor rechaza.
    newPassword: ['', [Validators.required, Validators.minLength(12)]],
    confirmPassword: ['', [Validators.required]],
  });

  ngOnInit(): void {
    this.http.get<Profile>(`${environment.apiBaseUrl}/me`).subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.isLoading.set(false);
      },
      error: () => {
        this.errorKey.set('profile.errors.load');
        this.isLoading.set(false);
      },
    });
  }

  protected get passwordsMatch(): boolean {
    const { newPassword, confirmPassword } = this.passwordForm.getRawValue();
    return newPassword === confirmPassword;
  }

  protected changePassword(): void {
    this.successKey.set(null);
    this.errorKey.set(null);

    if (this.passwordForm.invalid) {
      this.passwordForm.markAllAsTouched();
      return;
    }

    if (!this.passwordsMatch) {
      this.errorKey.set('profile.errors.mismatch');
      return;
    }

    this.isSaving.set(true);
    const { currentPassword, newPassword } = this.passwordForm.getRawValue();

    this.http
      .post<void>(`${environment.apiBaseUrl}/me/change-password`, {
        currentPassword,
        newPassword,
      })
      .subscribe({
        next: () => {
          this.isSaving.set(false);
          this.passwordForm.reset({ currentPassword: '', newPassword: '', confirmPassword: '' });
          this.successKey.set('profile.passwordChanged');
        },
        error: (error: { status?: number }) => {
          this.isSaving.set(false);
          // 401 aca no es sesion vencida sino contraseña actual incorrecta:
          // el interceptor no lo trata porque la peticion si estaba autenticada.
          this.errorKey.set(
            error.status === 401
              ? 'profile.errors.wrongPassword'
              : error.status === 400
                ? 'profile.errors.invalidPassword'
                : 'profile.errors.unexpected',
          );
        },
      });
  }
}
