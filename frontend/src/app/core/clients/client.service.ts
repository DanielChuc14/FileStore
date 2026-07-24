import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  Client,
  CreateClientRequest,
  CreateClientResult,
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

  create(request: CreateClientRequest): Observable<CreateClientResult> {
    return this.http.post<CreateClientResult>(this.baseUrl, request);
  }

  update(id: string, request: UpdateClientRequest): Observable<Client> {
    return this.http.patch<Client>(`${this.baseUrl}/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  resetPassword(id: string): Observable<{ password: string }> {
    return this.http.post<{ password: string }>(`${this.baseUrl}/${id}/reset-password`, {});
  }
}
