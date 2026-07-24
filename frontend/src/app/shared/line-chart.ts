import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  effect,
  input,
  viewChild,
} from '@angular/core';
import { Chart, ChartConfiguration, registerables } from 'chart.js';

// Chart.js v4 no registra nada por defecto para permitir tree-shaking. Se
// registra una sola vez al cargar el modulo.
Chart.register(...registerables);

export interface ChartSeries {
  label: string;
  data: number[];
  color: string;
}

/**
 * Grafica de lineas sobre un canvas. Se usa Chart.js directamente en lugar de
 * un wrapper: una dependencia menos y control total sobre el ciclo de vida.
 */
@Component({
  selector: 'app-line-chart',
  template: `
    <div class="relative h-64 w-full">
      <canvas #canvas></canvas>
      @if (labels().length === 0) {
        <p class="absolute inset-0 flex items-center justify-center text-sm text-slate-400">
          {{ emptyMessage() }}
        </p>
      }
    </div>
  `,
})
export class LineChart implements AfterViewInit, OnDestroy {
  readonly labels = input.required<string[]>();
  readonly series = input.required<ChartSeries[]>();
  readonly emptyMessage = input('Sin datos');

  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private chart?: Chart;

  constructor() {
    // El effect corre cuando cambian los inputs, pero solo actualiza si el
    // canvas ya existe: antes de AfterViewInit no hay nada que dibujar.
    effect(() => {
      const labels = this.labels();
      const series = this.series();

      if (this.chart) {
        this.chart.data.labels = labels;
        this.chart.data.datasets = this.toDatasets(series);
        this.chart.update();
      }
    });
  }

  ngAfterViewInit(): void {
    this.chart = new Chart(this.canvas().nativeElement, this.buildConfig());
  }

  ngOnDestroy(): void {
    // Sin destroy, Chart.js deja listeners de resize vivos y el canvas fugado.
    this.chart?.destroy();
  }

  private buildConfig(): ChartConfiguration {
    return {
      type: 'line',
      data: {
        labels: this.labels(),
        datasets: this.toDatasets(this.series()),
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        plugins: {
          legend: { position: 'bottom', labels: { usePointStyle: true, boxWidth: 8 } },
        },
        scales: {
          y: {
            beginAtZero: true,
            // Las series son conteos de eventos: no tiene sentido medio evento.
            ticks: { precision: 0 },
            grid: { color: 'rgba(148, 163, 184, 0.2)' },
          },
          x: {
            grid: { display: false },
            ticks: { maxRotation: 0, autoSkipPadding: 16 },
          },
        },
      },
    };
  }

  private toDatasets(series: ChartSeries[]) {
    return series.map((s) => ({
      label: s.label,
      data: s.data,
      borderColor: s.color,
      backgroundColor: s.color,
      tension: 0.3,
      pointRadius: 2,
      pointHoverRadius: 5,
      borderWidth: 2,
    }));
  }
}
