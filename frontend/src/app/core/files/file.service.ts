import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { Folder, PagedFiles, StoredFile } from './file.models';
import { FileVersion, TrashItem } from './trash.models';

@Injectable({ providedIn: 'root' })
export class FileService {
  private readonly http = inject(HttpClient);
  private readonly filesUrl = `${environment.apiBaseUrl}/files`;
  private readonly foldersUrl = `${environment.apiBaseUrl}/folders`;

  listFolders(parentId: string | null): Observable<Folder[]> {
    let params = new HttpParams();
    if (parentId) {
      params = params.set('parentId', parentId);
    }
    return this.http.get<Folder[]>(this.foldersUrl, { params });
  }

  listAllFolders(): Observable<Folder[]> {
    return this.http.get<Folder[]>(this.foldersUrl, {
      params: new HttpParams().set('all', true),
    });
  }

  createFolder(name: string, parentId: string | null): Observable<Folder> {
    return this.http.post<Folder>(this.foldersUrl, { name, parentId });
  }

  renameFolder(id: string, name: string): Observable<Folder> {
    return this.http.patch<Folder>(`${this.foldersUrl}/${id}`, { name });
  }

  deleteFolder(id: string, recursive: boolean): Observable<void> {
    return this.http.delete<void>(`${this.foldersUrl}/${id}`, {
      params: new HttpParams().set('recursive', recursive),
    });
  }

  listFiles(folderId: string | null, deleted = false): Observable<PagedFiles> {
    let params = new HttpParams().set('deleted', deleted);
    if (folderId) {
      params = params.set('folderId', folderId);
    }
    return this.http.get<PagedFiles>(this.filesUrl, { params });
  }

  upload(file: File, folderId: string | null): Observable<StoredFile> {
    const form = new FormData();
    form.append('file', file, file.name);

    let params = new HttpParams();
    if (folderId) {
      params = params.set('folderId', folderId);
    }

    // Sin Content-Type explicito: el navegador lo arma con el boundary del
    // multipart. Fijarlo a mano rompe la peticion.
    return this.http.post<StoredFile>(this.filesUrl, form, { params });
  }

  rename(id: string, name: string): Observable<StoredFile> {
    return this.http.patch<StoredFile>(`${this.filesUrl}/${id}`, { name });
  }

  move(id: string, folderId: string | null): Observable<StoredFile> {
    return this.http.patch<StoredFile>(`${this.filesUrl}/${id}`, {
      folderId,
      moveToRoot: folderId === null,
    });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.filesUrl}/${id}`);
  }

  /**
   * La descarga pide el blob y lo entrega al navegador. No se puede usar un
   * enlace directo porque la peticion necesita el header Authorization, que un
   * <a href> no puede enviar.
   */
  download(file: StoredFile): Observable<Blob> {
    return this.http.get(`${this.filesUrl}/${file.id}`, { responseType: 'blob' });
  }

  downloadVersion(fileId: string, versionNumber: number): Observable<Blob> {
    return this.http.get(`${this.filesUrl}/${fileId}`, {
      responseType: 'blob',
      params: new HttpParams().set('version', versionNumber),
    });
  }

  listVersions(fileId: string): Observable<FileVersion[]> {
    return this.http.get<FileVersion[]>(`${this.filesUrl}/${fileId}/versions`);
  }

  restoreVersion(fileId: string, versionNumber: number): Observable<void> {
    return this.http.post<void>(
      `${this.filesUrl}/${fileId}/versions/${versionNumber}/restore`,
      {},
    );
  }

  listTrash(): Observable<TrashItem[]> {
    return this.http.get<TrashItem[]>(`${environment.apiBaseUrl}/trash`);
  }

  restoreFromTrash(id: string): Observable<void> {
    return this.http.post<void>(`${environment.apiBaseUrl}/trash/${id}/restore`, {});
  }

  /** Irreversible: borra el binario y libera la cuota. */
  hardDelete(id: string): Observable<void> {
    return this.http.delete<void>(`${environment.apiBaseUrl}/trash/${id}`);
  }
}
