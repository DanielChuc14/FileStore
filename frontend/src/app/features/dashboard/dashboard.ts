import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

import { AuthService } from '../../core/auth/auth.service';
import { StatsService } from '../../core/stats/stats.service';
import { ClientStats, Usage } from '../../core/stats/stats.models';
import { FormatBytesPipe } from '../../shared/format-bytes.pipe';
import { ChartSeries, LineChart } from '../../shared/line-chart';

@Component({
  selector: 'app-dashboard',
  imports: [TranslatePipe, FormatBytesPipe, LineChart],
  templateUrl: './dashboard.html',
})
export class Dashboard implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly stats = inject(StatsService);

  protected readonly user = this.auth.user;
  protected readonly usage = signal<Usage | null>(null);
  protected readonly activity = signal<ClientStats | null>(null);
  protected readonly isLoading = signal(true);

  protected readonly chartLabels = computed(() =>
    // Se muestra solo dia y mes: la etiqueta completa no entra en el eje.
    (this.activity()?.daily ?? []).map((d) => d.date.slice(5).replace('-', '/')),
  );

  protected readonly chartSeries = computed<ChartSeries[]>(() => {
    const daily = this.activity()?.daily ?? [];

    return [
      { label: 'Subidas', data: daily.map((d) => d.uploads), color: '#0f172a' },
      { label: 'Descargas', data: daily.map((d) => d.downloads), color: '#0ea5e9' },
    ];
  });

  /** Color de la barra segun lo cerca que este del limite. */
  protected readonly usageBarClass = computed(() => {
    const percentage = this.usage()?.usedPercentage ?? 0;

    if (percentage >= 95) return 'bg-red-500';
    if (percentage >= 80) return 'bg-amber-500';
    return 'bg-slate-900';
  });

  ngOnInit(): void {
    this.stats.getUsage().subscribe({
      next: (usage) => {
        this.usage.set(usage);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false),
    });

    this.stats.getClientStats(30).subscribe({
      next: (activity) => this.activity.set(activity),
    });
  }
}
