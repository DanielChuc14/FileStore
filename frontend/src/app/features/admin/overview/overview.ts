import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

import { StatsService } from '../../../core/stats/stats.service';
import { AdminStats } from '../../../core/stats/stats.models';
import { FormatBytesPipe } from '../../../shared/format-bytes.pipe';
import { ChartSeries, LineChart } from '../../../shared/line-chart';

@Component({
  selector: 'app-admin-overview',
  imports: [TranslatePipe, FormatBytesPipe, LineChart],
  templateUrl: './overview.html',
})
export class AdminOverview implements OnInit {
  private readonly service = inject(StatsService);

  protected readonly stats = signal<AdminStats | null>(null);
  protected readonly isLoading = signal(true);

  protected readonly chartLabels = computed(() =>
    (this.stats()?.daily ?? []).map((d) => d.date.slice(5).replace('-', '/')),
  );

  protected readonly chartSeries = computed<ChartSeries[]>(() => {
    const daily = this.stats()?.daily ?? [];

    return [
      { label: 'Subidas', data: daily.map((d) => d.uploads), color: '#0f172a' },
      { label: 'Descargas', data: daily.map((d) => d.downloads), color: '#0ea5e9' },
    ];
  });

  ngOnInit(): void {
    this.service.getAdminStats(30).subscribe({
      next: (stats) => {
        this.stats.set(stats);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false),
    });
  }

  /** Porcentaje de cuota consumida por un cliente, acotado a 100 para la barra. */
  protected usagePercent(used: number, quota: number): number {
    if (quota === 0) {
      return 0;
    }
    return Math.min(Math.round((used / quota) * 100), 100);
  }
}
