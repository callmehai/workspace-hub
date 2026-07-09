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
}