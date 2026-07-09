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
  page?: number;
  limit?: number;
}



export const itemsApi = {
  getItems: async (params?: GetItemsParams): Promise<PagedResult<ItemResponse>> => {
    // indexes: null → serialize mảng thành "statuses=A&statuses=B" (không bracket),
    // đúng format ASP.NET Core [FromQuery] cần để bind IReadOnlyList<T>.
    const response = await api.get('/items', { params, paramsSerializer: { indexes: null } });
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



