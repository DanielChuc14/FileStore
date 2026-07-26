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

export interface UpdateClientRequest {
  name?: string;
  quotaBytes?: number;
  isActive?: boolean;
  trashRetentionDays?: number;
  maxFileSizeBytes?: number;
  clearTrashRetentionOverride?: boolean;
  clearMaxFileSizeOverride?: boolean;
}
