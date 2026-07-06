import React, { useState, useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { adminApi } from '../lib/adminApi';
import { handleApiError } from '../lib/errorUtils';
import { Users, UserCheck, Lock, AlertCircle, Loader2, ChevronLeft, ChevronRight, Search } from 'lucide-react';
import { PieChart, Pie, Cell, Tooltip, Legend, ResponsiveContainer } from 'recharts';
import { format } from 'date-fns';
import { vi } from 'date-fns/locale';

const COLORS = ['#10b981', '#ef4444', '#94a3b8']; // Active, Error, Disconnected

function useDebounce<T>(value: T, delay: number): T {
  const [debouncedValue, setDebouncedValue] = useState<T>(value);
  useEffect(() => {
    const handler = setTimeout(() => setDebouncedValue(value), delay);
    return () => clearTimeout(handler);
  }, [value, delay]);
  return debouncedValue;
}

export const AdminDashboard = () => {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const debouncedSearch = useDebounce(search, 350);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setPage(1);
  }, [debouncedSearch]);

  const { data: stats, isLoading: statsLoading, error: statsError } = useQuery({
    queryKey: ['admin', 'stats'],
    queryFn: adminApi.getStats,
  });

  const { data: usersData, isLoading: usersLoading, error: usersError, isPlaceholderData } = useQuery({
    queryKey: ['admin', 'users', { page, search: debouncedSearch }],
    queryFn: () => adminApi.getUsers({ page, limit: 20, search: debouncedSearch }),
    placeholderData: (prev) => prev,
  });

  useEffect(() => {
    if (statsError) handleApiError(statsError, 'Không thể tải thống kê');
  }, [statsError]);

  useEffect(() => {
    if (usersError) handleApiError(usersError, 'Không thể tải danh sách người dùng');
  }, [usersError]);

  const pieData = stats ? [
    { name: 'Active', value: stats.connectionsByStatus.Active ?? 0 },
    { name: 'Error', value: stats.connectionsByStatus.Error ?? 0 },
    { name: 'Disconnected', value: stats.connectionsByStatus.Disconnected ?? 0 },
  ] : [];

  return (
    <div className="p-5 md:p-8 max-w-7xl mx-auto text-slate-800">
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-slate-900 mb-1">Quản trị hệ thống</h1>
        <p className="text-sm text-slate-500">Giám sát hoạt động và quản lý người dùng</p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mb-8">
        <StatCard title="Tổng người dùng" value={stats?.totalUsers} icon={<Users className="w-5 h-5 text-indigo-600" />} isLoading={statsLoading} />
        <StatCard title="Đang hoạt động" value={stats?.activeUsers} icon={<UserCheck className="w-5 h-5 text-emerald-600" />} isLoading={statsLoading} />
        <StatCard title="Bị khóa" value={stats?.lockedUsers} icon={<Lock className="w-5 h-5 text-rose-600" />} isLoading={statsLoading} />
        <StatCard title="Lỗi đồng bộ (24h)" value={stats?.syncErrorsLast24h} icon={<AlertCircle className="w-5 h-5 text-amber-600" />} isLoading={statsLoading} />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 mb-8">
        <div className="bg-white p-5 rounded-xl border border-slate-200 shadow-sm col-span-1 lg:col-span-1">
          <h3 className="text-sm font-semibold text-slate-900 mb-4">Trạng thái kết nối</h3>
          {statsLoading ? (
             <div className="h-48 flex items-center justify-center">
               <Loader2 className="w-6 h-6 animate-spin text-slate-300" />
             </div>
          ) : statsError ? (
             <div className="h-48 flex items-center justify-center text-sm text-rose-500">
                Lỗi tải dữ liệu
             </div>
          ) : (
            <div className="h-48">
              <ResponsiveContainer width="100%" height="100%">
                <PieChart>
                  <Pie
                    data={pieData}
                    cx="50%"
                    cy="50%"
                    innerRadius={45}
                    outerRadius={65}
                    paddingAngle={2}
                    dataKey="value"
                  >
                    {pieData.map((_, index) => (
                      <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
                    ))}
                  </Pie>
                  <Tooltip contentStyle={{ borderRadius: '8px', border: '1px solid #e2e8f0', fontSize: '13px' }} />
                  <Legend verticalAlign="bottom" height={36} iconType="circle" wrapperStyle={{ fontSize: '12px' }} />
                </PieChart>
              </ResponsiveContainer>
            </div>
          )}
        </div>
      </div>

      <div className="bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden flex flex-col">
        <div className="p-4 border-b border-slate-200 flex items-center justify-between gap-4">
          <h3 className="text-base font-semibold text-slate-900">Danh sách người dùng</h3>
          <div className="relative w-64">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
            <input
              type="text"
              placeholder="Tìm kiếm email, tên..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="w-full pl-9 pr-3 py-1.5 text-sm bg-slate-50 border border-slate-200 rounded-md focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500"
            />
          </div>
        </div>

        <div className="overflow-x-auto min-h-[300px]">
          <table className="w-full text-left border-collapse">
            <thead>
              <tr className="bg-slate-50/50 border-b border-slate-200 text-xs font-semibold text-slate-500 uppercase tracking-wider">
                <th className="px-5 py-3">Người dùng</th>
                <th className="px-5 py-3">Vai trò</th>
                <th className="px-5 py-3">Trạng thái</th>
                <th className="px-5 py-3">Kết nối</th>
                <th className="px-5 py-3">Ngày tạo</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-200 text-sm">
              {usersLoading ? (
                <tr>
                  <td colSpan={5} className="px-5 py-10 text-center">
                    <Loader2 className="w-6 h-6 animate-spin text-indigo-600 mx-auto" />
                  </td>
                </tr>
              ) : usersError ? (
                <tr>
                  <td colSpan={5} className="px-5 py-8 text-center text-rose-500 text-sm">Lỗi tải danh sách người dùng</td>
                </tr>
              ) : usersData?.items.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-5 py-10 text-center text-slate-500 text-sm">Không tìm thấy người dùng nào phù hợp.</td>
                </tr>
              ) : (
                usersData?.items.map((u) => (
                  <tr key={u.id} className="hover:bg-slate-50/50 transition-colors">
                    <td className="px-5 py-3">
                      <div className="flex items-center gap-3">
                        <div className="w-8 h-8 rounded-full bg-indigo-100 text-indigo-700 flex items-center justify-center font-semibold text-xs shrink-0">
                          {u.fullName.charAt(0).toUpperCase()}
                        </div>
                        <div>
                          <div className="font-medium text-slate-900">{u.fullName}</div>
                          <div className="text-xs text-slate-500">{u.email}</div>
                        </div>
                      </div>
                    </td>
                    <td className="px-5 py-3">
                      <span className={`inline-flex px-2 py-0.5 rounded text-[11px] font-medium ${u.role === 'Admin' ? 'bg-purple-100 text-purple-700' : 'bg-slate-100 text-slate-700'}`}>
                        {u.role}
                      </span>
                    </td>
                    <td className="px-5 py-3">
                      <span className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[11px] font-medium ${u.isActive ? 'bg-emerald-50 text-emerald-700 border border-emerald-200' : 'bg-rose-50 text-rose-700 border border-rose-200'}`}>
                        <span className={`w-1.5 h-1.5 rounded-full ${u.isActive ? 'bg-emerald-500' : 'bg-rose-500'}`}></span>
                        {u.isActive ? 'Active' : 'Locked'}
                      </span>
                    </td>
                    <td className="px-5 py-3 text-slate-600">
                      {u.connectionCount}
                    </td>
                    <td className="px-5 py-3 text-slate-500 text-xs">
                      {format(new Date(u.createdAt), 'dd MMM yyyy, HH:mm', { locale: vi })}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {usersData && usersData.total > 0 && (
          <div className="px-5 py-3 border-t border-slate-200 bg-slate-50 flex items-center justify-between">
            <div className="text-xs text-slate-500">
              Trang <span className="font-medium text-slate-900">{usersData.page}</span> / <span className="font-medium text-slate-900">{Math.ceil(usersData.total / usersData.limit)}</span>
              {' '} ({usersData.total} người dùng)
            </div>
            <div className="flex gap-2">
              <button
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                disabled={usersData.page === 1 || isPlaceholderData}
                className="p-1 rounded-md border border-slate-200 bg-white text-slate-600 hover:bg-slate-50 disabled:opacity-50 transition-colors"
              >
                <ChevronLeft className="w-4 h-4" />
              </button>
              <button
                onClick={() => setPage((p) => p + 1)}
                disabled={usersData.page >= Math.ceil(usersData.total / usersData.limit) || isPlaceholderData}
                className="p-1 rounded-md border border-slate-200 bg-white text-slate-600 hover:bg-slate-50 disabled:opacity-50 transition-colors"
              >
                <ChevronRight className="w-4 h-4" />
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

const StatCard = ({ title, value, icon, isLoading }: { title: string; value?: number; icon: React.ReactNode; isLoading: boolean }) => (
  <div className="bg-white p-5 rounded-xl border border-slate-200 shadow-sm flex items-center gap-4">
    <div className="w-10 h-10 rounded-lg bg-slate-50 flex items-center justify-center shrink-0 border border-slate-100">
      {icon}
    </div>
    <div>
      <div className="text-sm text-slate-500 font-medium mb-0.5">{title}</div>
      <div className="text-2xl font-bold text-slate-900">
        {isLoading ? <div className="w-12 h-8 bg-slate-200 animate-pulse rounded"></div> : value ?? '-'}
      </div>
    </div>
  </div>
);
