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
  connectionsByStatus: {
    Active: number;
    Error: number;
    Disconnected: number;
  };
  totalItems: number;
  syncErrorsLast24h: number;
}

export interface GetAdminUsersRequest {
  page?: number;
  limit?: number;
  search?: string;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  limit: number;
}
