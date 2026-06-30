import api from './api';
import type { 
  PagedResult, ItemResponse, UpdateItemStatusRequest, CreateNoteRequest, 
  FolderResponse, AddItemToFolderRequest, ItemFolderResponse, ItemStatus, ItemType
} from '../types/items';


export interface GetItemsParams {
  folderId?: string;
  status?: ItemStatus;
  type?: ItemType;
  isImportant?: boolean;
  search?: string;
  page?: number;
  limit?: number;
}

export const itemsApi = {
  getItems: async (params?: GetItemsParams): Promise<PagedResult<ItemResponse>> => {
    const response = await api.get('/items', { params });
    return response.data;
  },
  
  updateItemStatus: async (id: string, request: UpdateItemStatusRequest): Promise<ItemResponse> => {
    const response = await api.patch(`/items/${id}/status`, request);
    return response.data;
  },

  updateItemImportant: async (id: string, isImportant: boolean): Promise<ItemResponse> => {
    const response = await api.patch(`/items/${id}/important`, { isImportant });
    return response.data;
  },

  createNote: async (request: CreateNoteRequest): Promise<ItemResponse> => {
    const response = await api.post('/items/note', request);
    return response.data;
  },
};

export const foldersApi = {
  getFolders: async (includeShared: boolean = false): Promise<FolderResponse[]> => {
    const response = await api.get('/folders', { params: { includeShared } });
    return response.data;
  },
  
  addItemToFolder: async (folderId: string, request: AddItemToFolderRequest): Promise<ItemFolderResponse> => {
    const response = await api.post(`/folders/${folderId}/items`, request);
    return response.data;
  },
  
  removeItemFromFolder: async (folderId: string, itemId: string): Promise<void> => {
    await api.delete(`/folders/${folderId}/items/${itemId}`);
  }
};
