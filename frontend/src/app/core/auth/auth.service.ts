import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, map, of, tap } from 'rxjs';

import { environment } from '../../../environments/environment';
import { AuthResponse, CurrentUser, LoginRequest } from './auth.models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/auth`;

  /**
   * El access token vive SOLO en memoria, nunca en localStorage ni
   * sessionStorage: si un XSS lograra ejecutarse, no tendria de donde leerlo.
   * El precio es que al recargar la pagina se pierde, y hay que recuperarlo
   * llamando a refresh() contra la cookie httpOnly.
   */
  private readonly accessToken = signal<string | null>(null);
  private readonly currentUser = signal<CurrentUser | null>(null);

  readonly user = this.currentUser.asReadonly();
  readonly isAuthenticated = computed(() => this.accessToken() !== null);
  readonly isSuperAdmin = computed(() => this.currentUser()?.role === 'SuperAdmin');

  getAccessToken(): string | null {
    return this.accessToken();
  }

  login(credentials: LoginRequest): Observable<CurrentUser> {
    return this.http
      .post<AuthResponse>(`${this.baseUrl}/login`, credentials, { withCredentials: true })
      .pipe(
        tap((response) => this.applySession(response)),
        map((response) => this.toUser(response)),
      );
  }

  /**
   * Pide un access token nuevo usando la cookie de refresh. `withCredentials`
   * es imprescindible: sin el, el navegador no adjunta la cookie porque la API
   * esta en otro origen.
   */
  refresh(): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${this.baseUrl}/refresh`, {}, { withCredentials: true })
      .pipe(tap((response) => this.applySession(response)));
  }

  /** Restaura la sesion al arrancar la app. Nunca falla: si no hay cookie, no hay sesion. */
  restoreSession(): Observable<boolean> {
    return this.refresh().pipe(
      map(() => true),
      catchError(() => {
        this.clearSession();
        return of(false);
      }),
    );
  }

  logout(): Observable<void> {
    return this.http
      .post<void>(`${this.baseUrl}/logout`, {}, { withCredentials: true })
      .pipe(
        tap(() => this.clearSession()),
        catchError(() => {
          // Aunque el servidor falle, la sesion local se limpia igual.
          this.clearSession();
          return of(void 0);
        }),
      );
  }

  clearSession(): void {
    this.accessToken.set(null);
    this.currentUser.set(null);
  }

  private applySession(response: AuthResponse): void {
    this.accessToken.set(response.accessToken);
    this.currentUser.set(this.toUser(response));
  }

  private toUser(response: AuthResponse): CurrentUser {
    return {
      userId: response.userId,
      email: response.email,
      role: response.role,
    };
  }
}
