import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  AdminStats,
  AllowedType,
  AppConfig,
  ClientStats,
  PagedAudit,
  Usage,
} from './stats.models';

@Injectable({ providedIn: 'root' })
export class StatsService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  getUsage(): Observable<Usage> {
    return this.http.get<Usage>(`${this.base}/me/usage`);
  }

  getClientStats(days = 30): Observable<ClientStats> {
    return this.http.get<ClientStats>(`${this.base}/me/stats`, {
      params: new HttpParams().set('days', days),
    });
  }

  getAuditLog(filters: {
    action?: string;
    resourceType?: string;
    from?: string;
    to?: string;
    page?: number;
    pageSize?: number;
  }): Observable<PagedAudit> {
    let params = new HttpParams()
      .set('page', filters.page ?? 1)
      .set('pageSize', filters.pageSize ?? 25);

    if (filters.action) params = params.set('action', filters.action);
    if (filters.resourceType) params = params.set('resourceType', filters.resourceType);
    if (filters.from) params = params.set('from', `${filters.from}T00:00:00Z`);
    if (filters.to) params = params.set('to', `${filters.to}T23:59:59Z`);

    return this.http.get<PagedAudit>(`${this.base}/me/audit-log`, { params });
  }

  getAdminStats(days = 30): Observable<AdminStats> {
    return this.http.get<AdminStats>(`${this.base}/admin/stats`, {
      params: new HttpParams().set('days', days),
    });
  }

  getConfig(): Observable<AppConfig> {
    return this.http.get<AppConfig>(`${this.base}/admin/config`);
  }

  updateConfig(config: Partial<AppConfig>): Observable<AppConfig> {
    return this.http.patch<AppConfig>(`${this.base}/admin/config`, config);
  }

  getAllowedTypes(): Observable<AllowedType[]> {
    return this.http.get<AllowedType[]>(`${this.base}/admin/allowed-types`);
  }

  updateAllowedType(id: string, isEnabled: boolean): Observable<AllowedType> {
    return this.http.patch<AllowedType>(`${this.base}/admin/allowed-types/${id}`, { isEnabled });
  }
}
