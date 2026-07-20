import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { ApiKey, CreateApiKeyRequest, CreateApiKeyResult } from './api-key.models';

@Injectable({ providedIn: 'root' })
export class ApiKeyService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/me/api-keys`;

  list(): Observable<ApiKey[]> {
    return this.http.get<ApiKey[]>(this.baseUrl);
  }

  create(request: CreateApiKeyRequest): Observable<CreateApiKeyResult> {
    return this.http.post<CreateApiKeyResult>(this.baseUrl, request);
  }

  update(id: string, request: { name?: string; rateLimitPerMinute?: number }): Observable<ApiKey> {
    return this.http.patch<ApiKey>(`${this.baseUrl}/${id}`, request);
  }

  rotate(id: string): Observable<CreateApiKeyResult> {
    return this.http.post<CreateApiKeyResult>(`${this.baseUrl}/${id}/rotate`, {});
  }

  revoke(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/revoke`, {});
  }
}
