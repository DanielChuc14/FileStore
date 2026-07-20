export interface ApiKey {
  id: string;
  name: string;
  prefix: string;
  rateLimitPerMinute: number | null;
  isActive: boolean;
  lastUsedAt: string | null;
  createdAt: string;
  revokedAt: string | null;
}

/** El valor completo llega una unica vez, al crear o al rotar. */
export interface CreateApiKeyResult {
  apiKey: ApiKey;
  value: string;
}

export interface CreateApiKeyRequest {
  name: string;
  rateLimitPerMinute?: number | null;
}
