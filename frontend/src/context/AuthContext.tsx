<<<<<<< HEAD
import { createContext, useContext, useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { api } from '../services/api';
import type { UserDto } from '../types/auth';

interface AuthContextType {
  user: UserDto | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (token: string, user: UserDto) => void;
  logout: () => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider = ({ children }: { children: ReactNode }) => {
  const [user, setUser] = useState<UserDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    const fetchMe = async () => {
      const token = localStorage.getItem('token');
      if (!token) {
        setIsLoading(false);
        return;
      }

      try {
        const response = await api.get<UserDto>('/auth/me');
        setUser(response.data);
      } catch (error) {
        console.error('Failed to fetch user', error);
        localStorage.removeItem('token');
      } finally {
        setIsLoading(false);
      }
    };

    fetchMe();
  }, []);

  const login = (token: string, userData: UserDto) => {
    localStorage.setItem('token', token);
    setUser(userData);
  };

  const logout = () => {
    localStorage.removeItem('token');
    setUser(null);
=======
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
>>>>>>> bf126b6ce92ddd724c2438e9d963105f3b70e1b9
  };

  return (
    <AuthContext.Provider
      value={{
<<<<<<< HEAD
        user,
=======
        user: user ?? null,
>>>>>>> bf126b6ce92ddd724c2438e9d963105f3b70e1b9
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
<<<<<<< HEAD

export const useAuth = () => {
  const context = useContext(AuthContext);
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
=======
>>>>>>> bf126b6ce92ddd724c2438e9d963105f3b70e1b9
