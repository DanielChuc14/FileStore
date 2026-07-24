export type UserRole = 'SuperAdmin' | 'Client';

export interface LoginRequest {
  email: string;
  password: string;
}

/**
 * Respuesta del backend. No trae el refresh token: ese va en la cookie
 * httpOnly, que este codigo no puede leer ni necesita leer.
 */
export interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  userId: string;
  email: string;
  role: UserRole;
}

export interface CurrentUser {
  userId: string;
  email: string;
  role: UserRole;
}
