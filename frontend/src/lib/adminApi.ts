import api from './api';
import type { AdminIntegrationDto, AdminStatsDto, AdminUserDto, GetAdminUsersRequest, PagedResult } from '../types/admin';

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

  getIntegrations: async (): Promise<AdminIntegrationDto[]> => {
    const response = await api.get<AdminIntegrationDto[]>('/admin/integrations');
    return response.data;
  },

  toggleIntegration: async (key: string, isEnabled: boolean): Promise<AdminIntegrationDto> => {
    const response = await api.patch<AdminIntegrationDto>(`/admin/integrations/${key}/enable`, { isEnabled });
    return response.data;
  },
};
