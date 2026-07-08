import React, { useState, useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { adminApi } from '../lib/adminApi';
import { handleApiError } from '../lib/errorUtils';
import { Users, UserCheck, Lock, AlertCircle, Loader2, ChevronLeft, ChevronRight, Search } from 'lucide-react';
import { PieChart, Pie, Cell, Tooltip, Legend, ResponsiveContainer } from 'recharts';
import { PageSizeSelect } from '../components/PageSizeSelect';
import { format } from 'date-fns';
import { vi, enUS } from 'date-fns/locale';
import { useI18n } from '../hooks/useI18n';
import { useTheme } from '../hooks/useTheme';

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
  const { t, lang } = useI18n();
  const { theme } = useTheme();
  const dfLocale = lang === 'vi' ? vi : enUS;
  const [page, setPage] = useState(1);
  const [limit, setLimit] = useState(20);
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
    queryKey: ['admin', 'users', { page, limit, search: debouncedSearch }],
    queryFn: () => adminApi.getUsers({ page, limit, search: debouncedSearch }),
    placeholderData: (prev) => prev,
  });

  useEffect(() => {
    if (statsError) handleApiError(statsError, t('admin.statsError'));
  }, [statsError, t]);

  useEffect(() => {
    if (usersError) handleApiError(usersError, t('admin.usersLoadError'));
  }, [usersError, t]);

    const pieData = stats ? [
    { name: t('integrations.statusActive'), value: stats.connectionsByStatus.Active ?? 0 },
    { name: t('integrations.statusError'), value: stats.connectionsByStatus.Error ?? 0 },
    { name: t('integrations.statusDisconnected'), value: stats.connectionsByStatus.Disconnected ?? 0 },
  ] : [];

  const pieTotal = pieData.reduce((sum, d) => sum + d.value, 0);

  const tooltipStyle = theme === 'dark'
    ? { background: '#1e293b', borderRadius: '8px', border: '1px solid #334155', color: '#e2e8f0', fontSize: '13px' }
    : { background: '#ffffff', borderRadius: '8px', border: '1px solid #e2e8f0', color: '#334155', fontSize: '13px', boxShadow: '0 4px 6px -1px rgb(0 0 0 / 0.1)' };

  const legendStyle = theme === 'dark'
    ? { fontSize: '12px', color: '#94a3b8' }
    : { fontSize: '12px', color: '#64748b' };

  return (
    <div className="p-5 md:p-8 max-w-7xl mx-auto text-slate-800 dark:text-slate-200">
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-slate-900 dark:text-slate-100 mb-1">{t('admin.title')}</h1>
        <p className="text-sm text-slate-500 dark:text-slate-400">{t('admin.subtitle')}</p>
      </div>

      <div className="grid grid-cols-2 md:grid-cols-4 gap-3 sm:gap-4 mb-8">
        <StatCard title={t('admin.totalUsers')} value={stats?.totalUsers} icon={<Users className="w-5 h-5 text-indigo-600" />} isLoading={statsLoading} />
        <StatCard title={t('admin.activeUsers')} value={stats?.activeUsers} icon={<UserCheck className="w-5 h-5 text-emerald-600" />} isLoading={statsLoading} />
        <StatCard title={t('admin.lockedUsers')} value={stats?.lockedUsers} icon={<Lock className="w-5 h-5 text-rose-600" />} isLoading={statsLoading} />
        <StatCard title={t('admin.syncErrors24h')} value={stats?.syncErrorsLast24h} icon={<AlertCircle className="w-5 h-5 text-amber-600" />} isLoading={statsLoading} />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 mb-8">
        <div className="bg-white dark:bg-slate-900 p-5 rounded-xl border border-slate-200 dark:border-slate-800 shadow-sm col-span-1 lg:col-span-1">
          <h3 className="text-sm font-semibold text-slate-900 dark:text-slate-100 mb-4">{t('admin.connectionStatus')}</h3>
          {statsLoading ? (
             <div className="h-48 flex items-center justify-center">
               <Loader2 className="w-6 h-6 animate-spin text-slate-300 dark:text-slate-500" />
             </div>
          ) : statsError ? (
             <div className="h-48 flex items-center justify-center text-sm text-rose-500">
                {t('admin.chartError')}
             </div>
          ) : pieTotal === 0 ? (
             <div className="h-48 flex items-center justify-center text-sm text-slate-500 dark:text-slate-400">
                {t('admin.chartEmpty')}
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
                  <Tooltip
                    contentStyle={tooltipStyle}
                    itemStyle={{ color: theme === 'dark' ? '#e2e8f0' : '#334155' }}
                  />
                  <Legend verticalAlign="bottom" height={36} iconType="circle" wrapperStyle={legendStyle} />
                </PieChart>
              </ResponsiveContainer>
            </div>
          )}
        </div>
      </div>

      <div className="bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 shadow-sm overflow-hidden flex flex-col">
        <div className="p-4 border-b border-slate-200 dark:border-slate-800 flex flex-col sm:flex-row sm:items-center justify-between gap-3 sm:gap-4">
          <h3 className="text-base font-semibold text-slate-900 dark:text-slate-100">{t('admin.userList')}</h3>
          <div className="relative w-full sm:w-64">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400 dark:text-slate-500" />
            <input
              type="text"
              placeholder={t('admin.searchPlaceholder')}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="w-full pl-9 pr-3 py-1.5 text-sm bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-md text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500"
            />
          </div>
        </div>

        <div className="overflow-x-auto min-h-[300px]">
          <table className="w-full text-left border-collapse">
            <thead>
              <tr className="bg-slate-50/50 dark:bg-slate-800/50 border-b border-slate-200 dark:border-slate-800 text-xs font-semibold text-slate-500 dark:text-slate-500 uppercase tracking-wider">
                <th className="px-5 py-3">{t('admin.colUser')}</th>
                <th className="px-5 py-3">{t('admin.colRole')}</th>
                <th className="px-5 py-3">{t('admin.colStatus')}</th>
                <th className="px-5 py-3">{t('admin.colConnections')}</th>
                <th className="px-5 py-3">{t('admin.colCreated')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-200 dark:divide-slate-800 text-sm">
              {usersLoading ? (
                <tr>
                  <td colSpan={5} className="px-5 py-10 text-center">
                    <Loader2 className="w-6 h-6 animate-spin text-indigo-600 mx-auto" />
                  </td>
                </tr>
              ) : usersError ? (
                <tr>
                  <td colSpan={5} className="px-5 py-8 text-center text-rose-500 text-sm">{t('admin.usersError')}</td>
                </tr>
              ) : usersData?.items.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-5 py-10 text-center text-slate-500 dark:text-slate-400 text-sm">{t('admin.noUsers')}</td>
                </tr>
              ) : (
                usersData?.items.map((u) => (
                  <tr key={u.id} className="hover:bg-slate-50/50 dark:hover:bg-slate-800/50 transition-colors">
                    <td className="px-5 py-3">
                      <div className="flex items-center gap-3">
                        <div className="w-8 h-8 rounded-full bg-indigo-100 dark:bg-indigo-500/10 text-indigo-700 dark:text-indigo-400 flex items-center justify-center font-semibold text-xs shrink-0">
                          {u.fullName.charAt(0).toUpperCase()}
                        </div>
                        <div>
                          <div className="font-medium text-slate-900 dark:text-slate-100">{u.fullName}</div>
                          <div className="text-xs text-slate-500 dark:text-slate-400">{u.email}</div>
                        </div>
                      </div>
                    </td>
                    <td className="px-5 py-3">
                      <span className={`inline-flex px-2 py-0.5 rounded text-[11px] font-medium ${u.role === 'Admin' ? 'bg-purple-100 text-purple-700 dark:bg-purple-500/10 dark:text-purple-400' : 'bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-200'}`}>
                        {u.role}
                      </span>
                    </td>
                    <td className="px-5 py-3">
                      <span className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[11px] font-medium ${u.isActive ? 'bg-emerald-50 text-emerald-700 border border-emerald-200 dark:bg-emerald-500/10 dark:text-emerald-400 dark:border-emerald-500/20' : 'bg-rose-50 text-rose-700 border border-rose-200 dark:bg-rose-500/10 dark:text-rose-400 dark:border-rose-500/20'}`}>
                        <span className={`w-1.5 h-1.5 rounded-full ${u.isActive ? 'bg-emerald-500' : 'bg-rose-500'}`}></span>
                        {u.isActive ? t('admin.userActive') : t('admin.userLocked')}
                      </span>
                    </td>
                    <td className="px-5 py-3 text-slate-600 dark:text-slate-400">
                      {u.connectionCount}
                    </td>
                    <td className="px-5 py-3 text-slate-500 dark:text-slate-400 text-xs">
                      {format(new Date(u.createdAt), 'dd MMM yyyy, HH:mm', { locale: dfLocale })}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {usersData && usersData.total > 0 && (
          <div className="px-4 sm:px-5 py-3 border-t border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-800 flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-3">
            <div className="flex items-center gap-3">
              <PageSizeSelect value={limit} onChange={(n) => { setLimit(n); setPage(1); }} />
              <div className="text-xs text-slate-500 dark:text-slate-400">
                {t('admin.pageLabel')} <span className="font-medium text-slate-900 dark:text-slate-100">{usersData.page}</span> / <span className="font-medium text-slate-900 dark:text-slate-100">{Math.ceil(usersData.total / usersData.limit)}</span>
                {' '} {t('admin.usersCount', { count: usersData.total })}
              </div>
            </div>
            <div className="flex gap-2">
              <button
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                disabled={usersData.page === 1 || isPlaceholderData}
                className="p-1 rounded-md border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-400 hover:bg-slate-50 dark:hover:bg-slate-800 disabled:opacity-50 transition-colors"
              >
                <ChevronLeft className="w-4 h-4" />
              </button>
              <button
                onClick={() => setPage((p) => p + 1)}
                disabled={usersData.page >= Math.ceil(usersData.total / usersData.limit) || isPlaceholderData}
                className="p-1 rounded-md border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-400 hover:bg-slate-50 dark:hover:bg-slate-800 disabled:opacity-50 transition-colors"
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
  <div className="bg-white dark:bg-slate-900 p-4 sm:p-5 rounded-xl border border-slate-200 dark:border-slate-800 shadow-sm flex items-center gap-3 sm:gap-4 min-w-0">
    <div className="w-9 h-9 sm:w-10 sm:h-10 rounded-lg bg-slate-50 dark:bg-slate-800 flex items-center justify-center shrink-0 border border-slate-100 dark:border-slate-800">
      {icon}
    </div>
    <div className="min-w-0">
      <div className="text-xs sm:text-sm text-slate-500 dark:text-slate-400 font-medium mb-0.5 truncate">{title}</div>
      <div className="text-xl sm:text-2xl font-bold text-slate-900 dark:text-slate-100">
        {isLoading ? <div className="w-12 h-8 bg-slate-200 dark:bg-slate-800 animate-pulse rounded"></div> : value ?? '-'}
      </div>
    </div>
  </div>
);
