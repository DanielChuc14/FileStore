import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';

import { FileService } from '../../core/files/file.service';
import { TrashItem } from '../../core/files/trash.models';
import { FormatBytesPipe } from '../../shared/format-bytes.pipe';

@Component({
  selector: 'app-trash',
  imports: [TranslatePipe, DatePipe, FormatBytesPipe],
  templateUrl: './trash.html',
})
export class Trash implements OnInit {
  private readonly service = inject(FileService);

  protected readonly items = signal<TrashItem[]>([]);
  protected readonly isLoading = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly pendingPurge = signal<TrashItem | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.service.listTrash().subscribe({
      next: (items) => {
        this.items.set(items);
        this.isLoading.set(false);
      },
      error: () => {
        this.errorKey.set('trash.errors.load');
        this.isLoading.set(false);
      },
    });
  }

  protected restore(item: TrashItem): void {
    this.errorKey.set(null);
    this.service.restoreFromTrash(item.id).subscribe({
      next: () => this.load(),
      error: (error: { status?: number }) => {
        // 409 significa que ya existe un archivo con ese nombre en el destino.
        this.errorKey.set(
          error.status === 409 ? 'trash.errors.nameTaken' : 'trash.errors.unexpected',
        );
      },
    });
  }

  protected confirmPurge(): void {
    const item = this.pendingPurge();
    if (!item) {
      return;
    }

    this.service.hardDelete(item.id).subscribe({
      next: () => {
        this.pendingPurge.set(null);
        this.load();
      },
      error: () => {
        this.pendingPurge.set(null);
        this.errorKey.set('trash.errors.unexpected');
      },
    });
  }
}
