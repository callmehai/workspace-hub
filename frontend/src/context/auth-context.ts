import { createContext } from 'react';
import type { UserDto } from '../types/auth';

export interface AuthContextType {
  user: UserDto | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (user: UserDto) => void;
  logout: () => void;
  /** Cập nhật cache user hiện tại (vd sau khi đổi/xoá avatar) mà không cần re-fetch /auth/me. */
  updateUser: (user: UserDto) => void;
}

export const AuthContext = createContext<AuthContextType | undefined>(undefined);
