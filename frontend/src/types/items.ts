export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  limit: number;
}

export type ItemType = 'Email' | 'Event' | 'File' | 'Note';
export type ItemStatus = 'Inbox' | 'Doing' | 'Done';

export interface ItemResponse {
  id: string;
  type: ItemType;
  title: string;
  snippet: string;
  status: ItemStatus;
  occurredAt: string;
  dueAt: string | null;
  isImportant: boolean;
  externalId: string | null;
  metadataJson: string | null;
}

export interface UpdateItemStatusRequest {
  status: ItemStatus;
}

export interface CreateNoteRequest {
  title: string;
  contentMarkdown: string;
  folderId?: string;
}

export interface FolderResponse {
  id: string;
  name: string;
  color: string | null;
  icon: string | null;
  sortOrder: number;
  isArchived: boolean;
  itemCount: number;
  isOwner: boolean;
  permission: string;
  ownerName: string;
}

export interface AddItemToFolderRequest {
  itemId: string;
}

export interface ItemFolderResponse {
  itemId: string;
  folderId: string;
  position: number;
  addedAt: string;
}
