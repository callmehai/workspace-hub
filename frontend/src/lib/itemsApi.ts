import api from './api';
import type { 
  PagedResult, ItemResponse, UpdateItemStatusRequest, CreateNoteRequest, 
  FolderResponse, AddItemToFolderRequest, ItemFolderResponse, ItemStatus, ItemType,
  PatchItemRequest, CreateEventRequest, CreateFolderRequest, UpdateFolderRequest,
  CreateTicketRequest
} from '../types/items';

export interface GetItemsParams {
  folderId?: string;
  statuses?: ItemStatus[];
  types?: ItemType[];
  isImportant?: boolean;
  search?: string;
  tagId?: string;
  projectKey?: string;
  /** Lọc theo Gmail label (INBOX/SENT/DRAFT/STARRED/IMPORTANT/CATEGORY_*) — chỉ áp cho Email. */
  gmailLabel?: string;
  /** Lọc ticket Jira theo người phụ trách (accountId); "unassigned" = chưa gán. */
  assignee?: string;
  connectionId?: string;
  page?: number;
  limit?: number;
}

/** 1 người phụ trách để filter Jira. accountId = "unassigned" khi ticket chưa gán. */
export interface JiraAssignee {
  accountId: string;
  displayName: string;
}

/** 1 comment của ticket Jira (2 chiều). */
export interface JiraComment {
  id: string;
  body: string;
  authorName: string;
  authorAccountId: string | null;
  created: string | null;
  updated: string | null;
}

/** 1 attachment (metadata) của ticket Jira. */
export interface JiraAttachment {
  id: string;
  filename: string;
  mimeType: string | null;
  size: number;
  authorName: string | null;
  created: string | null;
}



export const itemsApi = {
  getItems: async (params?: GetItemsParams): Promise<PagedResult<ItemResponse>> => {
    // indexes: null → serialize mảng thành "statuses=A&statuses=B" (không bracket),
    // đúng format ASP.NET Core [FromQuery] cần để bind IReadOnlyList<T>.
    const response = await api.get('/items', { params, paramsSerializer: { indexes: null } });
    return response.data;
  },
  
  /** Danh sách người phụ trách (assignee) suy từ ticket Jira đã sync — cho filter tab Jira. */
  getAssignees: async (): Promise<JiraAssignee[]> => {
    const response = await api.get('/items/assignees');
    return response.data;
  },

  getItemById: async (id: string): Promise<ItemResponse> => {
    const response = await api.get(`/items/${id}`);
    return response.data;
  },
  
  updateItemStatus: async (id: string, request: UpdateItemStatusRequest): Promise<ItemResponse> => {
    const response = await api.patch(`/items/${id}/status`, request);
    return response.data;
  },
  
  createNote: async (request: CreateNoteRequest): Promise<ItemResponse> => {
    const response = await api.post('/items/note', request);
    return response.data;
  },

  createEvent: async (request: CreateEventRequest): Promise<ItemResponse> => {
    const response = await api.post('/items/event', request);
    return response.data;
  },

  createTicket: async (request: CreateTicketRequest): Promise<ItemResponse> => {
    const response = await api.post('/items/ticket', request);
    return response.data;
  },

  patchItem: async (id: string, request: PatchItemRequest): Promise<ItemResponse> => {
    const response = await api.patch(`/items/${id}`, request);
    return response.data;
  },

  deleteItem: async (id: string): Promise<void> => {
    await api.delete(`/items/${id}`);
  },

  updateItemImportant: async (id: string, isImportant: boolean): Promise<ItemResponse> => {
    const response = await api.patch(`/items/${id}/important`, { isImportant });
    return response.data;
  },

  // ── Jira ticket: comment 2 chiều ──
  getComments: async (itemId: string): Promise<JiraComment[]> => {
    const res = await api.get(`/items/${itemId}/comments`);
    return res.data;
  },
  addComment: async (itemId: string, body: string, mediaIds?: string[]): Promise<JiraComment> => {
    const res = await api.post(`/items/${itemId}/comments`, { body, mediaIds });
    return res.data;
  },
  updateComment: async (itemId: string, commentId: string, body: string): Promise<JiraComment> => {
    const res = await api.put(`/items/${itemId}/comments/${commentId}`, { body });
    return res.data;
  },
  deleteComment: async (itemId: string, commentId: string): Promise<void> => {
    await api.delete(`/items/${itemId}/comments/${commentId}`);
  },

  // ── Jira ticket: attachment 2 chiều ──
  getAttachments: async (itemId: string): Promise<JiraAttachment[]> => {
    const res = await api.get(`/items/${itemId}/attachments`);
    return res.data;
  },
  uploadAttachment: async (itemId: string, file: File): Promise<JiraAttachment[]> => {
    const fd = new FormData();
    fd.append('file', file);
    const res = await api.post(`/items/${itemId}/attachments`, fd);
    return res.data;
  },
  deleteAttachment: async (itemId: string, attachmentId: string): Promise<void> => {
    await api.delete(`/items/${itemId}/attachments/${attachmentId}`);
  },
  /** URL tải trực tiếp (dùng cho <img> preview ảnh — cùng origin nên cookie auth tự gửi kèm). */
  attachmentUrl: (itemId: string, attachmentId: string): string =>
    `${api.defaults.baseURL ?? '/api'}/items/${itemId}/attachments/${attachmentId}/download`,
  /** Tải file: nhận blob rồi kích hoạt download với đúng filename. */
  downloadAttachment: async (itemId: string, attachmentId: string, filename: string): Promise<void> => {
    const res = await api.get(`/items/${itemId}/attachments/${attachmentId}/download`, { responseType: 'blob' });
    const url = window.URL.createObjectURL(res.data as Blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    window.URL.revokeObjectURL(url);
  },
};

export const foldersApi = {
  getFolders: async (includeShared: boolean = false): Promise<FolderResponse[]> => {
    const response = await api.get('/folders', { params: { includeShared } });
    return response.data;
  },
  
  createFolder: async (request: CreateFolderRequest): Promise<FolderResponse> => {
    const response = await api.post('/folders', request);
    return response.data;
  },

  updateFolder: async (id: string, request: UpdateFolderRequest): Promise<FolderResponse> => {
    const response = await api.put(`/folders/${id}`, request);
    return response.data;
  },

  deleteFolder: async (id: string): Promise<void> => {
    await api.delete(`/folders/${id}`);
  },

  addItemToFolder: async (folderId: string, request: AddItemToFolderRequest): Promise<ItemFolderResponse> => {
    const response = await api.post(`/folders/${folderId}/items`, request);
    return response.data;
  },
  
  removeItemFromFolder: async (folderId: string, itemId: string): Promise<void> => {
    await api.delete(`/folders/${folderId}/items/${itemId}`);
  },

  addItemsToFolderBulk: async (folderId: string, itemIds: string[]): Promise<void> => {
    await api.post(`/folders/${folderId}/items/bulk`, { itemIds });
  },

  removeItemsFromFolderBulk: async (folderId: string, itemIds: string[]): Promise<void> => {
    // using HTTP DELETE with a body requires config.data in axios
    await api.delete(`/folders/${folderId}/items/bulk`, { data: { itemIds } });
  }
};



