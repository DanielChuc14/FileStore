import { Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';

import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './reset-password.html',
})
export class ResetPassword {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  /** Token del enlace del correo. Sin el no hay nada que hacer en esta vista. */
  protected readonly token = this.route.snapshot.queryParamMap.get('token') ?? '';

  protected readonly isSubmitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly isDone = signal(false);

  protected readonly form = this.fb.nonNullable.group(
    {
      // El minimo coincide con el del backend: 12 caracteres.
      password: ['', [Validators.required, Validators.minLength(12)]],
      confirmation: ['', [Validators.required]],
    },
    { validators: [matchPasswords] },
  );

  protected submit(): void {
    if (!this.token || this.form.invalid || this.isSubmitting()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorKey.set(null);

    this.auth.resetPassword(this.token, this.form.getRawValue().password).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.isDone.set(true);
      },
      error: (error: { status?: number }) => {
        this.isSubmitting.set(false);
        // 401 es el enlace vencido o ya usado; 400 es una contraseña que no
        // cumple las reglas. Son problemas distintos y el usuario necesita
        // saber cual de los dos le toco.
        this.errorKey.set(
          error.status === 401
            ? 'resetPassword.errors.invalidToken'
            : error.status === 400
              ? 'resetPassword.errors.weakPassword'
              : 'resetPassword.errors.unexpected',
        );
      },
    });
  }

  protected goToLogin(): void {
    void this.router.navigateByUrl('/login');
  }
}

/** La confirmacion tiene que coincidir: escribir mal una contraseña que no se ve deja fuera de la cuenta. */
function matchPasswords(group: AbstractControl): { passwordMismatch: true } | null {
  const password = group.get('password')?.value;
  const confirmation = group.get('confirmation')?.value;

  return password && confirmation && password !== confirmation ? { passwordMismatch: true } : null;
}
