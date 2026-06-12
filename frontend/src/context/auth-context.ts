import { createContext } from 'react';
import type { UserDto } from '../types/auth';

export interface AuthContextType {
  user: UserDto | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (token: string, user: UserDto) => void;
  logout: () => void;
}

export const AuthContext = createContext<AuthContextType | undefined>(undefined);
