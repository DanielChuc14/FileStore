import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';

import { StatsService } from '../../core/stats/stats.service';
import { AuditEntry } from '../../core/stats/stats.models';

@Component({
  selector: 'app-audit',
  imports: [ReactiveFormsModule, TranslatePipe, DatePipe],
  templateUrl: './audit.html',
})
export class Audit implements OnInit {
  private readonly service = inject(StatsService);
  private readonly fb = inject(FormBuilder);

  protected readonly entries = signal<AuditEntry[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly totalPages = signal(0);
  protected readonly page = signal(1);
  protected readonly isLoading = signal(false);
  protected readonly expanded = signal<string | null>(null);

  /** Acciones mas frecuentes. La lista completa del enum seria inmanejable. */
  protected readonly actions = [
    'Upload',
    'Download',
    'Delete',
    'Restore',
    'HardDelete',
    'Move',
    'Rename',
    'RestoreVersion',
    'CreateFolder',
    'DeleteFolder',
    'CreateApiKey',
    'RotateApiKey',
    'RevokeApiKey',
    'Login',
  ];

  protected readonly filterForm = this.fb.nonNullable.group({
    action: [''],
    from: [''],
    to: [''],
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    const { action, from, to } = this.filterForm.getRawValue();

    this.service
      .getAuditLog({
        action: action || undefined,
        from: from || undefined,
        to: to || undefined,
        page: this.page(),
        pageSize: 25,
      })
      .subscribe({
        next: (result) => {
          this.entries.set(result.items);
          this.totalCount.set(result.totalCount);
          this.totalPages.set(result.totalPages);
          this.isLoading.set(false);
        },
        error: () => this.isLoading.set(false),
      });
  }

  protected applyFilters(): void {
    this.page.set(1);
    this.load();
  }

  protected clearFilters(): void {
    this.filterForm.reset({ action: '', from: '', to: '' });
    this.applyFilters();
  }

  protected goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) {
      return;
    }
    this.page.set(page);
    this.load();
  }

  protected toggleDetails(id: string): void {
    this.expanded.update((current) => (current === id ? null : id));
  }

  /** El metadata llega como JSON crudo; se formatea para que sea legible. */
  protected formatMetadata(json: string | null): string {
    if (!json) {
      return '';
    }

    try {
      return JSON.stringify(JSON.parse(json), null, 2);
    } catch {
      return json;
    }
  }
}
