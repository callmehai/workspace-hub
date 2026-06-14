import api from './api';
import type { 
  PagedResult, ItemResponse, UpdateItemStatusRequest, CreateNoteRequest, 
  FolderResponse, AddItemToFolderRequest, ItemFolderResponse 
} from '../types/items';


export const itemsApi = {
  getItems: async (params?: Record<string, any>): Promise<PagedResult<ItemResponse>> => {
    const response = await api.get('/items', { params });
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
