import type { ReactNode } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import api from '../lib/api';
import { authApi } from '../lib/authApi';
import type { UserDto } from '../types/auth';
import { AuthContext } from './auth-context';

/** Query key của user hiện tại — login/logout ghi đè cache key này. */
const ME_QUERY_KEY = ['auth', 'me'] as const;

export const AuthProvider = ({ children }: { children: ReactNode }) => {
  const queryClient = useQueryClient();

  // SCRUM-62: token nằm trong HttpOnly cookie → JS không kiểm tra được sự tồn tại.
  // Luôn gọi /auth/me; cookie hợp lệ → có user, không thì 401 (interceptor xử lý).
  const { data: user, isLoading } = useQuery({
    queryKey: ME_QUERY_KEY,
    queryFn: async () => (await api.get<UserDto>('/auth/me')).data,
    staleTime: Infinity,
    retry: false,
    // Khi chưa login, query này ở trạng thái error (401). Mặc định TanStack refetch on
    // window-focus → đổi tab quay lại sẽ bắn /auth/me lần nữa. Tắt để tránh refetch thừa
    // (login/logout đã chủ động set cache key này).
    refetchOnWindowFocus: false,
  });

  // Đăng nhập thành công: token đã được backend set vào cookie; chỉ cache user.
  const login = (userData: UserDto) => {
    queryClient.setQueryData(ME_QUERY_KEY, userData);
  };

  const updateUser = (userData: UserDto) => {
    queryClient.setQueryData(ME_QUERY_KEY, userData);
  };

  const logout = () => {
    // Báo backend xoá cookie auth (SCRUM-62); best-effort, không chặn cleanup local nếu lỗi.
    void authApi.logout().catch(() => undefined);
    queryClient.setQueryData(ME_QUERY_KEY, null);
    queryClient.clear();
  };

  return (
    <AuthContext.Provider
      value={{
        user: user ?? null,
        isAuthenticated: !!user,
        isLoading,
        login,
        logout,
        updateUser,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
};
