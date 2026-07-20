import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';

import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './login.html',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);

  protected readonly isSubmitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  protected submit(): void {
    if (this.form.invalid || this.isSubmitting()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorKey.set(null);

    this.auth.login(this.form.getRawValue()).subscribe({
      next: (user) => {
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
        const fallback = user.role === 'SuperAdmin' ? '/admin/clients' : '/dashboard';
        void this.router.navigateByUrl(returnUrl ?? fallback);
      },
      error: (error: { status?: number }) => {
        this.isSubmitting.set(false);
        // 401 es credenciales invalidas; cualquier otra cosa es un fallo tecnico.
        this.errorKey.set(
          error.status === 401 ? 'login.errors.invalidCredentials' : 'login.errors.unexpected',
        );
      },
    });
  }
}
