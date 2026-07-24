import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';

import { FileService } from '../../core/files/file.service';
import { Crumb, Folder, StoredFile } from '../../core/files/file.models';
import { FileVersion } from '../../core/files/trash.models';
import { FormatBytesPipe } from '../../shared/format-bytes.pipe';

@Component({
  selector: 'app-files',
  imports: [ReactiveFormsModule, TranslatePipe, DatePipe, FormatBytesPipe],
  templateUrl: './files.html',
})
export class Files implements OnInit {
  private readonly service = inject(FileService);
  private readonly fb = inject(FormBuilder);

  protected readonly folders = signal<Folder[]>([]);
  protected readonly files = signal<StoredFile[]>([]);
  protected readonly isLoading = signal(false);
  protected readonly isUploading = signal(false);
  protected readonly errorKey = signal<string | null>(null);

  /** Carpeta actual. null = raiz. */
  protected readonly currentFolderId = signal<string | null>(null);
  protected readonly breadcrumbs = signal<Crumb[]>([{ id: null, name: 'files.root' }]);

  protected readonly showFolderModal = signal(false);
  protected readonly pendingDeleteFile = signal<StoredFile | null>(null);
  protected readonly pendingDeleteFolder = signal<Folder | null>(null);

  /** Archivo cuyo historial de versiones se esta viendo. */
  protected readonly versionsFor = signal<StoredFile | null>(null);
  protected readonly versions = signal<FileVersion[]>([]);
  protected readonly isLoadingVersions = signal(false);

  protected readonly folderForm = this.fb.nonNullable.group({
    name: ['', [Validators.required]],
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.errorKey.set(null);

    const folderId = this.currentFolderId();

    this.service.listFolders(folderId).subscribe({
      next: (folders) => this.folders.set(folders),
      error: () => this.errorKey.set('files.errors.load'),
    });

    this.service.listFiles(folderId).subscribe({
      next: (result) => {
        this.files.set(result.items);
        this.isLoading.set(false);
      },
      error: () => {
        this.errorKey.set('files.errors.load');
        this.isLoading.set(false);
      },
    });
  }

  protected openFolder(folder: Folder): void {
    this.currentFolderId.set(folder.id);
    this.breadcrumbs.update((crumbs) => [...crumbs, { id: folder.id, name: folder.name }]);
    this.load();
  }

  protected navigateTo(index: number): void {
    const crumbs = this.breadcrumbs().slice(0, index + 1);
    this.breadcrumbs.set(crumbs);
    this.currentFolderId.set(crumbs[crumbs.length - 1].id);
    this.load();
  }

  protected createFolder(): void {
    if (this.folderForm.invalid) {
      this.folderForm.markAllAsTouched();
      return;
    }

    this.service.createFolder(this.folderForm.getRawValue().name, this.currentFolderId()).subscribe({
      next: () => {
        this.showFolderModal.set(false);
        this.folderForm.reset({ name: '' });
        this.load();
      },
      error: (error: { status?: number }) => {
        this.errorKey.set(
          error.status === 409 ? 'files.errors.folderExists' : 'files.errors.unexpected',
        );
      },
    });
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    this.isUploading.set(true);
    this.errorKey.set(null);

    this.service.upload(file, this.currentFolderId()).subscribe({
      next: () => {
        this.isUploading.set(false);
        input.value = '';
        this.load();
      },
      error: (error: { status?: number }) => {
        this.isUploading.set(false);
        input.value = '';
        // 413 es cuota o tamaño; 400 suele ser extension no permitida.
        this.errorKey.set(
          error.status === 413
            ? 'files.errors.tooLarge'
            : error.status === 400
              ? 'files.errors.notAllowed'
              : 'files.errors.unexpected',
        );
      },
    });
  }

  protected download(file: StoredFile): void {
    this.service.download(file).subscribe({
      next: (blob) => {
        // Se crea una URL temporal para el blob y se dispara un clic sintetico:
        // es la unica forma de descargar con un header Authorization.
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = file.originalName;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: () => this.errorKey.set('files.errors.download'),
    });
  }

  protected openVersions(file: StoredFile): void {
    this.versionsFor.set(file);
    this.isLoadingVersions.set(true);
    this.versions.set([]);

    this.service.listVersions(file.id).subscribe({
      next: (versions) => {
        this.versions.set(versions);
        this.isLoadingVersions.set(false);
      },
      error: () => {
        this.isLoadingVersions.set(false);
        this.errorKey.set('files.errors.unexpected');
      },
    });
  }

  protected downloadVersion(version: FileVersion): void {
    const file = this.versionsFor();
    if (!file) {
      return;
    }

    this.service.downloadVersion(file.id, version.versionNumber).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        // Se marca la version en el nombre para no pisar el archivo actual.
        link.download = `v${version.versionNumber}-${file.originalName}`;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: () => this.errorKey.set('files.errors.download'),
    });
  }

  protected restoreVersion(version: FileVersion): void {
    const file = this.versionsFor();
    if (!file) {
      return;
    }

    this.service.restoreVersion(file.id, version.versionNumber).subscribe({
      next: () => {
        this.openVersions(file);
        this.load();
      },
      error: () => this.errorKey.set('files.errors.unexpected'),
    });
  }

  protected confirmDeleteFile(): void {
    const file = this.pendingDeleteFile();
    if (!file) {
      return;
    }

    this.service.delete(file.id).subscribe(() => {
      this.pendingDeleteFile.set(null);
      this.load();
    });
  }

  protected confirmDeleteFolder(): void {
    const folder = this.pendingDeleteFolder();
    if (!folder) {
      return;
    }

    this.service.deleteFolder(folder.id, true).subscribe({
      next: () => {
        this.pendingDeleteFolder.set(null);
        this.load();
      },
      error: () => {
        this.pendingDeleteFolder.set(null);
        this.errorKey.set('files.errors.unexpected');
      },
    });
  }
}
