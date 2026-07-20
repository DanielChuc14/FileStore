export interface Folder {
  id: string;
  parentFolderId: string | null;
  name: string;
  path: string;
  createdAt: string;
}

export interface StoredFile {
  id: string;
  folderId: string | null;
  originalName: string;
  sizeBytes: number;
  mimeType: string;
  extension: string;
  versionCount: number;
  isDeleted: boolean;
  deletedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface PagedFiles {
  items: StoredFile[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
}

/** Migaja de pan para la navegacion por carpetas. */
export interface Crumb {
  id: string | null;
  name: string;
}
