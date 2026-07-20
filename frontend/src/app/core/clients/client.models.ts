export interface Client {
  id: string;
  email: string;
  name: string;
  quotaBytes: number;
  usedBytes: number;
  isActive: boolean;
  trashRetentionDays: number | null;
  maxFileSizeBytes: number | null;
  createdAt: string;
  updatedAt: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
}

export interface CreateClientRequest {
  email: string;
  name: string;
  quotaBytes: number;
  trashRetentionDays?: number | null;
  maxFileSizeBytes?: number | null;
}

/** La contraseña llega una unica vez, al crear o al resetear. */
export interface CreateClientResult {
  client: Client;
  generatedPassword: string;
}

export interface UpdateClientRequest {
  name?: string;
  quotaBytes?: number;
  isActive?: boolean;
  trashRetentionDays?: number;
  maxFileSizeBytes?: number;
  clearTrashRetentionOverride?: boolean;
  clearMaxFileSizeOverride?: boolean;
}
