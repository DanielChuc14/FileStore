import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';

import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-forgot-password',
  imports: [ReactiveFormsModule, TranslatePipe, RouterLink],
  templateUrl: './forgot-password.html',
})
export class ForgotPassword {
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);

  protected readonly isSubmitting = signal(false);

  /**
   * Se muestra el mismo acuse exista o no la cuenta. Es deliberado: si la vista
   * dijera "ese correo no esta registrado", el endpoint dejaria de servir de
   * nada al no filtrar esa informacion en la respuesta HTTP.
   */
  protected readonly isDone = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  protected submit(): void {
    if (this.form.invalid || this.isSubmitting()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);

    this.auth.forgotPassword(this.form.getRawValue().email).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.isDone.set(true);
      },
      // Incluso ante un fallo tecnico se muestra el acuse: distinguirlo daria
      // una via lateral para deducir si el correo existe.
      error: () => {
        this.isSubmitting.set(false);
        this.isDone.set(true);
      },
    });
  }
}
