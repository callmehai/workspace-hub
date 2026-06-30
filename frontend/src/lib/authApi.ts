import api from './api';
import type { AuthResponse, GoogleAuthStartResponse } from '../types/auth';

/**
 * Auth API — Google Sign-In (đăng nhập bằng Google, KHÁC connect-để-sync) + logout.
 * Sign-in dùng route callback riêng `/auth/google/callback` để không đụng flow
 * connect service ở `/oauth/callback`.
 */
export const authApi = {
  /** Lấy authorization URL của Google Sign-In (state lưu server-side để chống CSRF). */
  googleStart: async (): Promise<GoogleAuthStartResponse> => {
    const res = await api.post<GoogleAuthStartResponse>('/auth/google/start');
    return res.data;
  },

  /** Đổi code + state lấy JWT + thông tin user. */
  googleCallback: async (code: string, state: string): Promise<AuthResponse> => {
    const res = await api.post<AuthResponse>('/auth/google/callback', { code, state });
    return res.data;
  },

  /** Logout stateless (MVP) — backend trả 204, client tự xoá token. */
  logout: async (): Promise<void> => {
    await api.post('/auth/logout');
  },
};
