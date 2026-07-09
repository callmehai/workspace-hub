import api from './api';
import type { UserDto } from '../types/auth';

/** Hồ sơ user hiện tại — đổi tên, đổi mật khẩu, avatar (SCRUM-75, Cloudflare R2). */
export const usersApi = {
  updateProfile: async (fullName: string): Promise<UserDto> => {
    const res = await api.patch<UserDto>('/users/me', { fullName });
    return res.data;
  },

  changePassword: async (currentPassword: string, newPassword: string): Promise<void> => {
    await api.post('/users/me/change-password', { currentPassword, newPassword });
  },

  uploadAvatar: async (file: File): Promise<UserDto> => {
    const form = new FormData();
    form.append('file', file);
    const res = await api.post<UserDto>('/users/me/avatar', form);
    return res.data;
  },

  deleteAvatar: async (): Promise<UserDto> => {
    const res = await api.delete<UserDto>('/users/me/avatar');
    return res.data;
  },
};
