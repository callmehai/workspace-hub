import type { ReactNode } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import api, { tokenStore } from '../lib/api';
import type { UserDto } from '../types/auth';
import { AuthContext } from './auth-context';

/** Query key của user hiện tại — login/logout ghi đè cache key này. */
const ME_QUERY_KEY = ['auth', 'me'] as const;

export const AuthProvider = ({ children }: { children: ReactNode }) => {
  const queryClient = useQueryClient();

  // 401 đã được interceptor trong lib/api xử lý (clear token + về /login).
  const { data: user, isLoading } = useQuery({
    queryKey: ME_QUERY_KEY,
    queryFn: async () => (await api.get<UserDto>('/auth/me')).data,
    enabled: tokenStore.get() !== null,
    staleTime: Infinity,
    retry: false,
  });

  const login = (token: string, userData: UserDto) => {
    tokenStore.set(token);
    queryClient.setQueryData(ME_QUERY_KEY, userData);
  };

  const logout = () => {
    tokenStore.clear();
    queryClient.setQueryData(ME_QUERY_KEY, null);
  };

  return (
    <AuthContext.Provider
      value={{
        user: user ?? null,
        isAuthenticated: !!user,
        isLoading,
        login,
        logout,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
};
