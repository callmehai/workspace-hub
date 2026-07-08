import api from './api';
import type { NotificationDto } from '../types/notifications';
import type { PaginatedResponse } from './scheduledEmailsApi';

export const notificationsApi = {
  getNotifications: async (
    skip = 0,
    top = 20,
    unreadOnly = false,
  ): Promise<PaginatedResponse<NotificationDto>> => {
    let url = `/Notifications?$top=${top}&$skip=${skip}&$count=true&$orderby=createdAt desc`;
    if (unreadOnly) {
      url += '&$filter=isRead eq false';
    }
    const response = await api.get(url);
    const data = response.data;
    if (data && typeof data === 'object' && Array.isArray(data.value)) {
      return data as PaginatedResponse<NotificationDto>;
    }
    if (Array.isArray(data)) {
      return { value: data, '@odata.count': data.length };
    }
    return { value: [], '@odata.count': 0 };
  },

  getUnreadCount: async (): Promise<number> => {
    const response = await api.get('/Notifications?$filter=isRead eq false&$count=true&$top=0');
    const data = response.data;
    if (typeof data?.['@odata.count'] === 'number') {
      return data['@odata.count'];
    }
    if (Array.isArray(data?.value)) {
      return data.value.length;
    }
    return 0;
  },

  markAsRead: async (id: string): Promise<void> => {
    await api.patch(`/notifications/${id}/read`);
  },

  markAllAsRead: async (): Promise<void> => {
    await api.post('/notifications/read-all');
  },

  devSeed: async (): Promise<NotificationDto> => {
    const response = await api.post('/notifications/dev/seed');
    return response.data;
  },
};
