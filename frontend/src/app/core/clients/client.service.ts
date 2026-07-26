import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  Client,
  CreateClientRequest,
  PagedResult,
  UpdateClientRequest,
} from './client.models';

@Injectable({ providedIn: 'root' })
export class ClientService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/admin/clients`;

  list(search: string, page: number, pageSize: number): Observable<PagedResult<Client>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search.trim()) {
      params = params.set('search', search.trim());
    }
    return this.http.get<PagedResult<Client>>(this.baseUrl, { params });
  }

  getById(id: string): Observable<Client> {
    return this.http.get<Client>(`${this.baseUrl}/${id}`);
  }

  /** La contraseña generada no vuelve aca: se le envia por correo al cliente. */
  create(request: CreateClientRequest): Observable<Client> {
    return this.http.post<Client>(this.baseUrl, request);
  }

  update(id: string, request: UpdateClientRequest): Observable<Client> {
    return this.http.patch<Client>(`${this.baseUrl}/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  /** Genera una contraseña nueva y se la manda por correo al cliente. */
  resetPassword(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/reset-password`, {});
  }
}
