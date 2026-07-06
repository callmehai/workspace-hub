export type { PagedResult } from './items';

export interface AdminUserDto {
  id: string;
  email: string;
  fullName: string;
  role: string;
  isActive: boolean;
  lastLoginAt: string | null;
  createdAt: string;
  connectionCount: number;
  itemCount: number;
}

export interface AdminStatsDto {
  totalUsers: number;
  activeUsers: number;
  lockedUsers: number;
  totalConnections: number;
  connectionsByStatus: Partial<Record<'Active' | 'Error' | 'Disconnected', number>>;
  totalItems: number;
  syncErrorsLast24h: number;
}

export interface GetAdminUsersRequest {
  page?: number;
  limit?: number;
  search?: string;
}

