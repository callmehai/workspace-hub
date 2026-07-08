export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  limit: number;
}

export type ItemType = 'Email' | 'Event' | 'File' | 'Note' | 'Ticket';
export type ItemStatus = 'Inbox' | 'Doing' | 'Done';

/** Tag rút gọn nhúng trong item (BE trả kèm mỗi item — SCRUM-71). */
export interface ItemTag {
  id: string;
  name: string;
  color: string;
}

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
  folderIds: string[];
  tags: ItemTag[];
  connectionId?: string | null;
  threadId?: string | null;
  /** Số message trong thread (Email gộp thread). 1 = thư đơn. */
  threadCount?: number;
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

export interface CreateFolderRequest {
  name: string;
  color: string;
  icon: string;
}

export interface UpdateFolderRequest {
  name: string;
  color: string;
  icon: string;
  sortOrder: number;
  isArchived: boolean;
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

export interface PatchItemRequest {
  isUnread?: boolean;
  isStarred?: boolean;
  addLabels?: string[];
  removeLabels?: string[];
  isTrashed?: boolean;
  title?: string;
  start?: string; // ISO DateTime
  end?: string; // ISO DateTime
  location?: string;
  attendees?: string[];
  name?: string;
  statusTransition?: string;
}

export interface CreateEventRequest {
  connectionId: string;
  title: string;
  start: string;
  end: string;
  location?: string;
  attendees?: string[];
}

// ── Tags (SCRUM-70/71) ──
export interface TagResponse {
  id: string;
  name: string;
  color: string;
  itemCount: number;
}

export interface CreateTagRequest {
  name: string;
  color: string;
}

export interface UpdateTagRequest {
  name: string;
  color: string;
}
