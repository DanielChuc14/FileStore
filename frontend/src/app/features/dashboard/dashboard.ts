import { Component, inject } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

import { AuthService } from '../../core/auth/auth.service';

/** Placeholder. El dashboard real se construye en la Fase 7. */
@Component({
  selector: 'app-dashboard',
  imports: [TranslatePipe],
  template: `
    <h1 class="mb-2 text-xl font-semibold">{{ 'dashboard.title' | translate }}</h1>
    <p class="text-sm text-slate-600">
      {{ 'dashboard.welcome' | translate: { email: user()?.email } }}
    </p>
  `,
})
export class Dashboard {
  private readonly auth = inject(AuthService);
  protected readonly user = this.auth.user;
}
