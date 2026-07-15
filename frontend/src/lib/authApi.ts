import api from './api';
import type { AuthResultDto, GoogleAuthStartResponse, RegisterResult } from '../types/auth';

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

  /** Đổi code + state → set cookie auth (SCRUM-62); body trả user + expiresIn. */
  googleCallback: async (code: string, state: string): Promise<AuthResultDto> => {
    const res = await api.post<AuthResultDto>('/auth/google/callback', { code, state });
    return res.data;
  },

  /** Logout — backend xoá cookie auth (SCRUM-62), trả 204. */
  logout: async (): Promise<void> => {
    await api.post('/auth/logout');
  },

  /** SCRUM-64: đăng ký → tạo user (chưa verify) + gửi OTP. KHÔNG đăng nhập ngay. */
  register: async (body: { fullName: string; email: string; password: string; inviteToken?: string }): Promise<RegisterResult> => {
    const res = await api.post<RegisterResult>('/auth/register', body);
    return res.data;
  },

  /** SCRUM-64: gửi lại OTP cho email chưa verify → trả cooldown (giây). */
  sendOtp: async (email: string): Promise<number> => {
    const res = await api.post<{ resendCooldownSeconds: number }>('/auth/send-otp', { email });
    return res.data.resendCooldownSeconds;
  },

  /** SCRUM-64: verify OTP → set cookie auth (đăng nhập); trả user. */
  verifyOtp: async (email: string, code: string): Promise<AuthResultDto> => {
    const res = await api.post<AuthResultDto>('/auth/verify-otp', { email, code });
    return res.data;
  },
};
