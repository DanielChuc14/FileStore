export interface TrashItem {
  id: string;
  originalName: string;
  sizeBytes: number;
  extension: string;
  deletedAt: string;
  purgeAt: string;
  daysUntilPurge: number;
}

export interface FileVersion {
  id: string;
  versionNumber: number;
  sizeBytes: number;
  mimeType: string;
  checksumSha256: string;
  isCurrent: boolean;
  createdAt: string;
}
