import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { connectionsApi, type ConnectionDto } from '../lib/connectionsApi';
import { integrationsApi } from '../lib/integrationsApi';
import { isAxiosError } from 'axios';
import { handleApiError } from '../lib/errorUtils';
import type { ApiErrorResponse } from '../lib/errorUtils';
import { useI18n } from '../hooks/useI18n';
import type { TranslationKey } from '../i18n/translations';
import toast from 'react-hot-toast';
import { Loader2, Plus, RefreshCw, AlertCircle } from 'lucide-react';
import { formatDistanceToNow } from 'date-fns';
import { vi, enUS } from 'date-fns/locale';
import type { Locale } from 'date-fns';
import { usePollingInterval } from '../hooks/usePollingInterval';
import { useMemo } from 'react';

const SERVICES: {
  integrationKey: string; provider: string; serviceType: string;
  name: string; descKey: TranslationKey; icon: string; bgColor: string;
}[] = [
    {
      integrationKey: 'google',
      provider: 'google',
      serviceType: 'Gmail',
      name: 'Google Gmail',
      descKey: 'integrations.descGmail',
      icon: '/icons/gmail.svg',
      bgColor: 'bg-gray-50',
    },
    {
      integrationKey: 'google',
      provider: 'google',
      serviceType: 'GCal',
      name: 'Google Calendar',
      descKey: 'integrations.descGCal',
      icon: '/icons/gcal.svg',
      bgColor: 'bg-gray-50',
    },
    {
      integrationKey: 'google',
      provider: 'google',
      serviceType: 'Drive',
      name: 'Google Drive',
      descKey: 'integrations.descDrive',
      icon: '/icons/drive.svg',
      bgColor: 'bg-gray-50',
    },
    {
      integrationKey: 'atlassian',
      provider: 'atlassian',
      serviceType: 'Jira',
      name: 'Atlassian Jira',
      descKey: 'integrations.descJira',
      icon: '/icons/jira.svg',
      bgColor: 'bg-gray-50',
    },
  ];

/** BE trả key i18n (giống notifications.*) — dịch tại trang, không map global trong errorUtils. */
const INTEGRATION_I18N_PREFIX = 'integrations.';

function integrationApiMessage(
  err: unknown,
  t: (key: TranslationKey, vars?: Record<string, string | number>) => string,
  vars?: Record<string, string | number>,
): string | null {
  if (!isAxiosError(err)) return null;
  const msg = (err.response?.data as ApiErrorResponse | undefined)?.message?.trim();
  if (msg?.startsWith(INTEGRATION_I18N_PREFIX)) {
    return t(msg as TranslationKey, vars);
  }
  return null;
}

type StatusMeta = { bg: string; fg: string; dot: string; label: string };

/** Màu + nhãn cho status của MỘT connection (account). Connection luôn đã kết nối nên chỉ
 *  Active / Error / (Disconnected...) — trạng thái "admin tắt" là mức service, xử lý riêng. */
function statusMeta(status: string, t: (k: TranslationKey) => string): StatusMeta {
  const s = status.toLowerCase();
  if (s === 'active') {
    return { bg: 'bg-green-100 dark:bg-green-500/15', fg: 'text-green-700 dark:text-green-300', dot: 'bg-green-500', label: t('integrations.statusActive') };
  }
  if (s === 'error') {
    return { bg: 'bg-red-100 dark:bg-red-500/15', fg: 'text-red-700 dark:text-red-300', dot: 'bg-red-500', label: t('integrations.statusError') };
  }
  return { bg: 'bg-yellow-100 dark:bg-yellow-500/15', fg: 'text-yellow-700 dark:text-yellow-300', dot: 'bg-yellow-500', label: status };
}

/** 1 dòng account (connection) trong 1 card service: nhãn account + trạng thái + Sync/Ngắt/Kết nối lại. */
function AccountRow({
  conn, dfLocale, busy,
  onSync, onDisconnect, onReconnect, isSyncing,
}: {
  conn: ConnectionDto;
  dfLocale: Locale;
  busy: boolean;
  isSyncing: boolean;
  onSync: (id: string) => void;
  onDisconnect: (id: string) => void;
  onReconnect: () => void;
}) {
  const { t } = useI18n();
  const status = conn.status || 'Disconnected';
  const isActive = status.toLowerCase() === 'active';
  const isError = status.toLowerCase() === 'error';
  const meta = statusMeta(status, t);
  const lastSyncedText = conn.lastSyncedAt
    ? t('integrations.syncedAgo', { ago: formatDistanceToNow(new Date(conn.lastSyncedAt), { addSuffix: true, locale: dfLocale }) })
    : t('integrations.neverSynced');

  return (
    <div className={`flex items-center gap-2.5 rounded-lg border px-3 py-2 ${isError ? 'border-red-200 dark:border-red-900/50 bg-red-50/40 dark:bg-red-500/5' : 'border-gray-100 dark:border-slate-800 bg-slate-50/70 dark:bg-slate-800/40'}`}>
      <div className="flex-1 min-w-0">
        <div className="text-[13px] font-medium text-gray-800 dark:text-slate-200 truncate">
          {conn.providerAccountId || conn.id}
        </div>
        <div className="text-[11px] text-gray-400 dark:text-slate-500 truncate">{lastSyncedText}</div>
      </div>

      <div className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[11px] font-medium ${meta.bg} ${meta.fg} flex-shrink-0`}>
        <span className={`w-1.5 h-1.5 rounded-full ${meta.dot}`}></span>
        {meta.label}
      </div>

      <div className="flex items-center gap-1.5 flex-shrink-0">
        {isActive && (
          <button
            onClick={() => onSync(conn.id)}
            disabled={busy}
            title={t('toolbar.sync')}
            className="inline-flex items-center justify-center h-7 w-7 border border-gray-200 rounded-lg bg-white text-gray-600 hover:bg-gray-50 hover:border-gray-300 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-300 dark:hover:bg-slate-700 transition-colors disabled:opacity-50"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isSyncing ? 'animate-spin' : ''}`} />
          </button>
        )}
        {isError && (
          <button
            onClick={onReconnect}
            disabled={busy}
            className="inline-flex items-center gap-1 h-7 px-2.5 border border-transparent rounded-lg bg-brand-600 text-white text-[12px] font-medium hover:bg-brand-700 transition-colors disabled:opacity-50"
          >
            {t('integrations.reconnect')}
          </button>
        )}
        <button
          onClick={() => onDisconnect(conn.id)}
          disabled={busy}
          className="inline-flex items-center h-7 px-2.5 border border-gray-200 rounded-lg bg-white text-gray-600 text-[12px] font-medium hover:border-red-500 hover:text-red-600 hover:bg-red-50 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-300 dark:hover:bg-red-500/10 dark:hover:text-red-400 dark:hover:border-red-500/50 transition-colors disabled:opacity-50"
        >
          {t('integrations.disconnect')}
        </button>
      </div>
    </div>
  );
}

export const Integrations = () => {
  const queryClient = useQueryClient();
  const pollMs = usePollingInterval(60_000);
  const { t, lang } = useI18n();
  const dfLocale = lang === 'vi' ? vi : enUS;

  const { data: connections = [], isLoading: loading, isError, refetch, isFetching } = useQuery({
    queryKey: ['connections'],
    queryFn: connectionsApi.getConnections,
    retry: false,
    refetchInterval: pollMs,
    refetchIntervalInBackground: true,
    refetchOnWindowFocus: true,
  });

  const { data: integrationCatalog = [] } = useQuery({
    queryKey: ['integrations', 'catalog'],
    queryFn: integrationsApi.getCatalog,
    staleTime: 60_000,
  });

  const integrationEnabledByKey = useMemo(() => {
    const map = new Map<string, boolean>();
    for (const row of integrationCatalog) {
      map.set(row.key.toLowerCase(), row.isEnabled);
    }
    return map;
  }, [integrationCatalog]);

  const disconnectMutation = useMutation({
    mutationFn: connectionsApi.disconnect,
    onSuccess: () => {
      toast.success(t('integrations.disconnected'));
      queryClient.invalidateQueries({ queryKey: ['connections'] });
      // Disconnect XOÁ THẬT mọi item của connection (ConnectionsService.DisconnectAsync
      // → DeleteByConnectionIdAsync). Không invalidate thì list vẫn hiện ticket đã bị xoá
      // (item ma), trong khi dropdown người phụ trách đọc mới nên trống → trông như lỗi.
      queryClient.invalidateQueries({ queryKey: ['items'] });
      queryClient.invalidateQueries({ queryKey: ['jira'] });
    },
    onError: (err) => handleApiError(err, t('integrations.disconnectFail')),
  });

  const connectMutation = useMutation({
    mutationFn: ({ integrationKey, serviceType, redirectUri }: {
      integrationKey: string;
      serviceType: string;
      redirectUri: string;
      serviceName: string;
    }) => connectionsApi.startOAuth({ integrationKey, serviceType, redirectUri }),
    onSuccess: (res) => {
      window.location.assign(res.authorizationUrl);
    },
    onError: (err, variables) => {
      const localized = integrationApiMessage(err, t, { name: variables.serviceName });
      if (localized) {
        toast.error(localized);
        return;
      }
      handleApiError(err, t('integrations.connectFail'));
    },
  });

  const syncMutation = useMutation({
    mutationFn: connectionsApi.syncConnection,
    onSuccess: () => {
      toast.success(t('integrations.syncRequested'));
      queryClient.invalidateQueries({ queryKey: ['connections'] });
      queryClient.invalidateQueries({ queryKey: ['items'] });
      // Metadata Jira (project + assignee) suy từ ticket vừa sync → phải refetch cùng.
      queryClient.invalidateQueries({ queryKey: ['jira'] });
    },
    onError: (err) => {
      // 502 đã ghi Status=Error trên BE — invalidate để badge «Lỗi đồng bộ» hiện ngay, không cần F5.
      handleApiError(err, t('integrations.syncFail'));
      queryClient.invalidateQueries({ queryKey: ['connections'] });
    },
  });

  const handleConnect = (integrationKey: string, serviceType: string, serviceName: string) => {
    const redirectUri = `${window.location.origin}/oauth/callback`;
    connectMutation.mutate({ integrationKey, serviceType, redirectUri, serviceName });
  };


  return (
    <div className="p-5 md:p-8 max-w-5xl mx-auto text-gray-800 dark:text-slate-200">
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100 mb-1">
          {t('integrations.title')}
          {isFetching && !loading && <Loader2 className="w-4 h-4 animate-spin inline-block ml-2 text-gray-400" />}
        </h1>
        <p className="text-sm text-gray-500 dark:text-slate-400">
          {t('integrations.subtitle')}
        </p>
      </div>

      {loading ? (
        <div className="flex justify-center items-center py-20">
          <Loader2 className="w-8 h-8 animate-spin text-brand-600" />
        </div>
      ) : isError ? (
        <div className="bg-white dark:bg-slate-900 rounded-xl border border-gray-100 dark:border-slate-800 shadow-sm p-12 flex flex-col items-center justify-center text-center">
          <div className="w-12 h-12 bg-red-50 dark:bg-red-500/10 text-red-500 rounded-2xl flex items-center justify-center mb-4">
            <AlertCircle className="w-6 h-6" />
          </div>
          <h3 className="text-base font-semibold text-gray-900 dark:text-slate-100 mb-1">{t('integrations.loadError')}</h3>
          <p className="text-sm text-gray-500 dark:text-slate-400 mb-6">{t('integrations.loadErrorHint')}</p>
          <button
            onClick={() => refetch()}
            className="h-9 px-6 bg-brand-600 hover:bg-brand-700 text-white text-[13px] font-medium rounded-lg transition-colors shadow-sm"
          >
            {t('common.retry')}
          </button>
        </div>
      ) : (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
          {SERVICES.map((service) => {
            // Mô hình B đa tài khoản: 1 service có thể có NHIỀU connection (account) — lấy tất cả.
            const serviceConnections = connections.filter(
              (c) => c.provider.toLowerCase() === service.provider.toLowerCase() &&
                c.serviceType.toLowerCase() === service.serviceType.toLowerCase()
            );
            const hasAny = serviceConnections.length > 0;
            // Chỉ Google cho phép nhiều tài khoản/service. Jira (atlassian) KHÔNG multi-connection (chốt scope)
            // → không hiện nút "Thêm tài khoản".
            const allowMultiAccount = service.integrationKey.toLowerCase() === 'google';
            const integrationEnabled = integrationEnabledByKey.get(service.integrationKey.toLowerCase()) ?? true;
            // Admin tắt integration: chỉ chặn khi CHƯA có account nào (đã có thì vẫn cho quản lý/thêm).
            const connectBlocked = !integrationEnabled && !hasAny;
            const hasConnError = serviceConnections.some((c) => c.status.toLowerCase() === 'error');

            const connectPending =
              connectMutation.isPending && connectMutation.variables?.serviceType === service.serviceType;

            return (
              <div key={`${service.integrationKey}-${service.serviceType}`} className={`bg-white dark:bg-slate-900 rounded-xl border flex flex-col p-5 shadow-sm transition-shadow hover:shadow-md ${hasConnError ? 'border-red-200 dark:border-red-900/50' : 'border-gray-200 dark:border-slate-800'}`}>

                <div className="flex items-start gap-3.5 mb-3.5">
                  <div className={`w-11 h-11 rounded-xl ${service.bgColor} dark:bg-slate-800 flex items-center justify-center border border-gray-100 dark:border-slate-700 flex-shrink-0`}>
                    <img src={service.icon} alt={service.name} className="w-6 h-6 object-contain drop-shadow-sm" />
                  </div>

                  <div className="flex-1 min-w-0">
                    <div className="text-[15px] font-semibold text-gray-900 dark:text-slate-100">{service.name}</div>
                    <div className="text-[12.5px] text-gray-500 dark:text-slate-400 truncate">
                      {hasAny ? t('integrations.accountsCount', { n: serviceConnections.length }) : t('integrations.noAccount')}
                    </div>
                  </div>
                </div>

                <div className="text-[13px] text-gray-600 dark:text-slate-400 leading-relaxed mb-4">
                  {connectBlocked ? (
                    <span className="text-amber-700 dark:text-amber-300">{t('integrations.disabledHint')}</span>
                  ) : (
                    t(service.descKey)
                  )}
                </div>

                {/* Danh sách account đã kết nối — mỗi account 1 dòng, Sync/Ngắt/Kết nối lại riêng. */}
                {hasAny && (
                  <div className="space-y-2 mb-4">
                    {serviceConnections.map((conn) => {
                      const rowBusy =
                        (disconnectMutation.isPending && disconnectMutation.variables === conn.id) ||
                        (syncMutation.isPending && syncMutation.variables === conn.id);
                      const rowSyncing = syncMutation.isPending && syncMutation.variables === conn.id;
                      return (
                        <AccountRow
                          key={conn.id}
                          conn={conn}
                          dfLocale={dfLocale}
                          busy={rowBusy}
                          isSyncing={rowSyncing}
                          onSync={(id) => syncMutation.mutate(id)}
                          onDisconnect={(id) => disconnectMutation.mutate(id)}
                          onReconnect={() => handleConnect(service.integrationKey, service.serviceType, service.name)}
                        />
                      );
                    })}
                  </div>
                )}

                {/* Footer — Kết nối (chưa có account) / Thêm tài khoản (đã có) / Admin tắt. */}
                <div className="mt-auto pt-1 flex items-center justify-end">
                  {connectBlocked ? (
                    <span className="text-xs text-amber-700 dark:text-amber-300 font-medium">
                      {t('integrations.statusDisabledByAdmin')}
                    </span>
                  ) : !integrationEnabled ? (
                    // Đã có account nhưng admin tắt → không cho thêm mới (account cũ vẫn quản lý ở trên).
                    <span className="text-xs text-amber-700 dark:text-amber-300 font-medium">
                      {t('integrations.statusDisabledByAdmin')}
                    </span>
                  ) : (!hasAny || allowMultiAccount) ? (
                    <button
                      onClick={() => handleConnect(service.integrationKey, service.serviceType, service.name)}
                      disabled={connectPending}
                      className="inline-flex items-center gap-1.5 h-8 px-3 border border-transparent rounded-lg bg-brand-600 text-white text-xs font-medium hover:bg-brand-700 transition-colors disabled:opacity-50"
                    >
                      {connectPending ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Plus className="w-3.5 h-3.5" />}
                      <span>{hasAny ? t('integrations.addAccount') : t('integrations.connect')}</span>
                    </button>
                  ) : null}
                </div>
              </div>
            );
          })}
        </div>
      )}

    </div>
  );
};
