import api from './api';
import type { AdminStatsDto, AdminUserDto, GetAdminUsersRequest, PagedResult } from '../types/admin';

export const adminApi = {
  getStats: async (): Promise<AdminStatsDto> => {
    const response = await api.get<AdminStatsDto>('/admin/stats');
    return response.data;
  },

  getUsers: async (params: GetAdminUsersRequest): Promise<PagedResult<AdminUserDto>> => {
    const response = await api.get<PagedResult<AdminUserDto>>('/admin/users', { params });
    return response.data;
  },

  toggleUserActive: async (id: string): Promise<AdminUserDto> => {
    const response = await api.post<AdminUserDto>(`/admin/users/${id}/toggle-active`);
    return response.data;
  },
};
