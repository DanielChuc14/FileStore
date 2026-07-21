export interface Usage {
  usedBytes: number;
  quotaBytes: number;
  filesCount: number;
  trashCount: number;
  trashBytes: number;
  usedPercentage: number;
}

export interface DailyActivity {
  date: string;
  uploads: number;
  downloads: number;
}

export interface ClientStats {
  daily: DailyActivity[];
}

export interface TopClient {
  id: string;
  email: string;
  name: string;
  usedBytes: number;
  quotaBytes: number;
}

export interface AdminStats {
  totalClients: number;
  activeClients: number;
  blockedClients: number;
  totalUsedBytes: number;
  totalQuotaBytes: number;
  totalFiles: number;
  filesInTrash: number;
  topClients: TopClient[];
  daily: DailyActivity[];
}

export interface AuditEntry {
  id: string;
  action: string;
  actorType: string;
  actorId: string;
  resourceType: string | null;
  resourceId: string | null;
  metadataJson: string | null;
  ipAddress: string | null;
  createdAt: string;
}

export interface PagedAudit {
  items: AuditEntry[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
}

export interface AppConfig {
  maxFileSizeBytes: number;
  trashRetentionDays: number;
  rateLimitDefaultPerMinute: number;
}

export interface AllowedType {
  id: string;
  extension: string;
  mimeType: string;
  isEnabled: boolean;
}
