import api from './api';
import type { TagResponse, CreateTagRequest, UpdateTagRequest } from '../types/items';

/**
 * Client cho Tag API (SCRUM-70 BE). Tag = label private của user.
 *  - CRUD: GET/POST/PUT/DELETE /api/tags
 *  - Gắn/gỡ tag khỏi item: POST /api/tags/{id}/items · DELETE /api/tags/{id}/items/{itemId}
 */
export const tagsApi = {
  getTags: async (): Promise<TagResponse[]> => {
    const res = await api.get('/tags');
    return res.data;
  },

  createTag: async (request: CreateTagRequest): Promise<TagResponse> => {
    const res = await api.post('/tags', request);
    return res.data;
  },

  updateTag: async (id: string, request: UpdateTagRequest): Promise<TagResponse> => {
    const res = await api.put(`/tags/${id}`, request);
    return res.data;
  },

  deleteTag: async (id: string): Promise<void> => {
    await api.delete(`/tags/${id}`);
  },

  /** Gắn tag vào item. */
  assignTag: async (tagId: string, itemId: string): Promise<void> => {
    await api.post(`/tags/${tagId}/items`, { itemId });
  },

  /** Gỡ tag khỏi item. */
  unassignTag: async (tagId: string, itemId: string): Promise<void> => {
    await api.delete(`/tags/${tagId}/items/${itemId}`);
  },
};
