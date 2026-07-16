import type { ItemResponse } from './items';

/** Quyền share Google Drive — khớp BE DrivePermissionDto (camelCase JSON). */
export type DrivePermissionRole = 'reader' | 'commenter' | 'writer';

export interface DrivePermission {
  id: string;
  type: string;           // user | anyone | domain | group
  role: string;           // reader | commenter | writer | owner
  emailAddress?: string | null;
  displayName?: string | null;
  isOwner: boolean;
  isLink: boolean;        // true = "ai có link"
}

export interface DrivePermissionsListResponse {
  items: DrivePermission[];
}

/** POST /api/drive/folders */
export interface CreateDriveFolderPayload {
  connectionId: string;
  name: string;
  parentItemId?: string | null;
}

/** POST /api/drive/items/{itemId}/permissions */
export interface AddDrivePermissionPayload {
  email: string;
  role: DrivePermissionRole;
  notify?: boolean;
}

/** PATCH /api/drive/items/{itemId}/permissions/{permissionId} */
export interface UpdateDrivePermissionPayload {
  role: DrivePermissionRole;
}

/** PUT /api/drive/items/{itemId}/link-sharing */
export interface LinkSharingPayload {
  enabled: boolean;
  role?: DrivePermissionRole | null;
  /** Case 1: user đã confirm popup tắt link cả folder mẹ. */
  confirmRestrictParent?: boolean;
}

/**
 * Case 1 — GET restrict-conflict / body 409 PUT link-sharing.
 * Khớp BE DriveLinkRestrictConflict.
 */
export interface DriveLinkRestrictConflict {
  code: string;
  itemId: string;
  itemTitle: string;
  itemExternalId: string;
  parentItemId?: string | null;
  parentExternalId: string;
  parentTitle: string;
  itemFromAccess: string;
  itemToAccess: string;
  parentFromAccess: string;
  parentToAccess: string;
}

export const LINK_RESTRICT_AFFECTS_PARENT = 'LINK_RESTRICT_AFFECTS_PARENT';

// ── Upload file/folder (bước 5 FE) — khớp BE DriveUploadLimits ──

/** Giới hạn 1 file — khớp BE DriveUploadLimits.MaxFileBytes */
export const MAX_DRIVE_FILE_BYTES = 100 * 1024 * 1024;

/** Giới hạn số file khi upload folder */
export const MAX_DRIVE_FOLDER_FILES = 200;

/** Tổng dung lượng tối đa khi upload folder */
export const MAX_DRIVE_FOLDER_TOTAL_BYTES = 500 * 1024 * 1024;

/** POST /api/drive/files — upload một file từ máy */
export interface UploadDriveFilePayload {
  connectionId: string;
  file: File;
  parentItemId?: string | null;
}

/** Một file trong batch upload folder — path từ File.webkitRelativePath */
export interface DriveFolderUploadEntry {
  file: File;
  /** Đường dẫn tương đối kể cả tên file, vd. DuAn/docs/readme.pdf */
  relativePath: string;
}

/** POST /api/drive/folders/upload */
export interface UploadDriveFolderPayload {
  connectionId: string;
  entries: DriveFolderUploadEntry[];
  parentItemId?: string | null;
}

/** Response POST /api/drive/folders/upload — khớp BE DriveFolderUploadResponse */
export interface DriveFolderUploadResponse {
  items: ItemResponse[];
  filesUploaded: number;
  foldersCreated: number;
}
