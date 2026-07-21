export type FolderSharePermission = 'Viewer' | 'Editor';

export interface FolderShareDto {
  shareId: string;
  folderId: string;
  folderName: string;
  sharedWithUserId: string;
  sharedWithUserName: string;
  sharedWithUserAvatar: string | null;
  permission: FolderSharePermission;
  status: 'Pending' | 'Accepted' | 'Declined';
  sharedAt: string;
}

export interface SharedFolderDto {
  shareId: string;
  folderId: string;
  folderName: string;
  ownerUserId: string;
  ownerName: string;
  permission: FolderSharePermission;
  status: 'Pending' | 'Accepted' | 'Declined';
  sharedAt: string;
}

export interface InviteFolderShareRequest {
  friendUserId: string;
  permission: FolderSharePermission;
}

export interface UpdateFolderShareRequest {
  permission: FolderSharePermission;
}
