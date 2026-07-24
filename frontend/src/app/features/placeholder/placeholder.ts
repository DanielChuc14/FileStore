import { Component, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

/**
 * Vista provisional para las secciones que llegan en fases posteriores.
 * Recibe por dato de ruta la clave del titulo y la fase en que se construye.
 */
@Component({
  selector: 'app-placeholder',
  imports: [TranslatePipe],
  template: `
    <div class="flex min-h-64 flex-col items-center justify-center rounded-lg border border-dashed border-slate-300 p-10 text-center">
      <h1 class="mb-2 text-lg font-semibold text-slate-700">{{ titleKey() | translate }}</h1>
      <p class="text-sm text-slate-500">
        {{ 'placeholder.comingSoon' | translate: { phase: phase() } }}
      </p>
    </div>
  `,
})
export class Placeholder {
  readonly titleKey = input.required<string>();
  readonly phase = input.required<number>();
}
