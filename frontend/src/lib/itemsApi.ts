import api from './api';
import type { 
  PagedResult, ItemResponse, UpdateItemStatusRequest, CreateNoteRequest, 
  FolderResponse, AddItemToFolderRequest, ItemFolderResponse, ItemStatus, ItemType,
  PatchItemRequest, CreateEventRequest, CreateTicketRequest
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

export interface JiraProject {
  id: string;
  key: string;
  name: string;
}

export interface JiraIssueType {
  id: string;
  name: string;
  subtask: boolean;
}

export interface JiraPriority {
  id: string;
  name: string;
}

export interface JiraUser {
  accountId: string;
  displayName: string;
  email?: string;
  active: boolean;
}

export interface JiraTransition {
  id: string;
  name: string;
  toStatusName: string;
}

export const itemsApi = {
  getItems: async (params?: GetItemsParams): Promise<PagedResult<ItemResponse>> => {
    const response = await api.get('/items', { params });
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
  }
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

export const jiraApi = {
  getProjects: async (connectionId: string): Promise<JiraProject[]> => {
    const response = await api.get('/jira/projects', { params: { connectionId } });
    return response.data;
  },
  getIssueTypes: async (connectionId: string, projectKey: string): Promise<JiraIssueType[]> => {
    const response = await api.get('/jira/issue-types', { params: { connectionId, projectKey } });
    return response.data;
  },
  getPriorities: async (connectionId: string): Promise<JiraPriority[]> => {
    const response = await api.get('/jira/priorities', { params: { connectionId } });
    return response.data;
  },
  getAssignableUsers: async (connectionId: string, projectKey: string, query?: string): Promise<JiraUser[]> => {
    const response = await api.get('/jira/assignable-users', { params: { connectionId, projectKey, query } });
    return response.data;
  },
  getTransitions: async (connectionId: string, itemId: string): Promise<JiraTransition[]> => {
    const response = await api.get('/jira/transitions', { params: { connectionId, itemId } });
    return response.data;
  }
};

