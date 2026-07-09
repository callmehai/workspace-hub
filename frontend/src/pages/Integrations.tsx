import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { connectionsApi } from '../lib/connectionsApi';
import { handleApiError } from '../lib/errorUtils';
import { useI18n } from '../hooks/useI18n';
import type { TranslationKey } from '../i18n/translations';
import toast from 'react-hot-toast';
import { Loader2, Plus, RefreshCw, AlertCircle } from 'lucide-react';
import { formatDistanceToNow } from 'date-fns';
import { vi, enUS } from 'date-fns/locale';
import { usePollingInterval } from '../hooks/usePollingInterval';
import { useState } from 'react';
import { CreateDriveFolderModal } from '../components/drive/CreateDriveFolderModal';

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

export const Integrations = () => {
  const queryClient = useQueryClient();
  const pollMs = usePollingInterval(60_000);
  const { t, lang } = useI18n();
  const [driveFolderModalOpen, setDriveFolderModalOpen] = useState(false);
  const [driveFolderConnId, setDriveFolderConnId] = useState<string | undefined>();
  const dfLocale = lang === 'vi' ? vi : enUS;

  const { data: connections = [], isLoading: loading, isError, refetch, isFetching } = useQuery({
    queryKey: ['connections'],
    queryFn: connectionsApi.getConnections,
    retry: false,
    refetchInterval: pollMs,
    refetchIntervalInBackground: true,
    refetchOnWindowFocus: true,
  });

  const disconnectMutation = useMutation({
    mutationFn: connectionsApi.disconnect,
    onSuccess: () => {
      toast.success(t('integrations.disconnected'));
      queryClient.invalidateQueries({ queryKey: ['connections'] });
    },
    onError: (err) => handleApiError(err, t('integrations.disconnectFail')),
  });

  const connectMutation = useMutation({
    mutationFn: (params: { integrationKey: string; serviceType: string; redirectUri: string }) =>
      connectionsApi.startOAuth(params),
    onSuccess: (res) => {
      window.location.assign(res.authorizationUrl);
    },
    onError: (err) => handleApiError(err, t('integrations.connectFail')),
  });

  const syncMutation = useMutation({
    mutationFn: connectionsApi.syncConnection,
    onSuccess: () => {
      toast.success(t('integrations.syncRequested'));
      queryClient.invalidateQueries({ queryKey: ['connections'] });
      queryClient.invalidateQueries({ queryKey: ['items'] });
    },
    onError: (err) => handleApiError(err, t('integrations.syncFail')),
  });

  const handleConnect = (integrationKey: string, serviceType: string) => {
    const redirectUri = `${window.location.origin}/oauth/callback`;
    connectMutation.mutate({ integrationKey, serviceType, redirectUri });
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
            const connection = connections.find(
              (c) => c.provider.toLowerCase() === service.provider.toLowerCase() &&
                c.serviceType.toLowerCase() === service.serviceType.toLowerCase()
            );

            const isConnected = !!connection;
            const status = connection?.status || 'Disconnected';
            const isActive = status.toLowerCase() === 'active';
            const isConnectionError = status.toLowerCase() === 'error';

            let statusBg = 'bg-gray-100 dark:bg-slate-700';
            let statusFg = 'text-gray-600 dark:text-slate-300';
            let statusDot = 'bg-gray-400';
            let statusLabel = t('integrations.statusDisconnected');

            if (isConnected) {
              if (isActive) {
                statusBg = 'bg-green-100 dark:bg-green-500/15';
                statusFg = 'text-green-700 dark:text-green-300';
                statusDot = 'bg-green-500';
                statusLabel = t('integrations.statusActive');
              } else if (isConnectionError) {
                statusBg = 'bg-red-100 dark:bg-red-500/15';
                statusFg = 'text-red-700 dark:text-red-300';
                statusDot = 'bg-red-500';
                statusLabel = t('integrations.statusError');
              } else {
                statusBg = 'bg-yellow-100 dark:bg-yellow-500/15';
                statusFg = 'text-yellow-700 dark:text-yellow-300';
                statusDot = 'bg-yellow-500';
                statusLabel = status;
              }
            }

            const lastSyncedText = connection?.lastSyncedAt
              ? t('integrations.syncedAgo', { ago: formatDistanceToNow(new Date(connection.lastSyncedAt), { addSuffix: true, locale: dfLocale }) })
              : t('integrations.neverSynced');

            const isLoadingAction =
              (disconnectMutation.isPending && disconnectMutation.variables === connection?.id) ||
              (syncMutation.isPending && syncMutation.variables === connection?.id) ||
              (connectMutation.isPending && connectMutation.variables?.serviceType === service.serviceType);

            return (
              <div key={`${service.integrationKey}-${service.serviceType}`} className={`bg-white dark:bg-slate-900 rounded-xl border flex flex-col p-5 shadow-sm transition-shadow hover:shadow-md ${isConnectionError ? 'border-red-200 dark:border-red-900/50' : 'border-gray-200 dark:border-slate-800'}`}>

                <div className="flex items-start gap-3.5 mb-3.5">
                  <div className={`w-11 h-11 rounded-xl ${service.bgColor} dark:bg-slate-800 flex items-center justify-center border border-gray-100 dark:border-slate-700 flex-shrink-0`}>
                    <img src={service.icon} alt={service.name} className="w-6 h-6 object-contain drop-shadow-sm" />
                  </div>

                  <div className="flex-1 min-w-0">
                    <div className="text-[15px] font-semibold text-gray-900 dark:text-slate-100">{service.name}</div>
                    <div className="text-[12.5px] text-gray-500 dark:text-slate-400 truncate">
                      {isConnected && connection.providerAccountId ? connection.providerAccountId : t('integrations.noAccount')}
                    </div>
                  </div>

                  <div className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium ${statusBg} ${statusFg} flex-shrink-0`}>
                    <span className={`w-1.5 h-1.5 rounded-full ${statusDot}`}></span>
                    {statusLabel}
                  </div>
                </div>

                <div className="text-[13px] text-gray-600 dark:text-slate-400 leading-relaxed mb-4">
                  {t(service.descKey)}
                </div>

                <div className="mt-auto pt-2 flex items-center justify-between gap-3">
                  <span className="text-xs text-gray-400 dark:text-slate-500 truncate min-w-0">
                    {isConnected ? lastSyncedText : ''}
                  </span>

                  <div className="flex items-center gap-2 flex-shrink-0">
                    {isConnected ? (
                      <>
                        {isActive && (
                          <>
                            {service.serviceType === 'Drive' && (
                              <button
                                type="button"
                                onClick={() => {
                                  setDriveFolderConnId(connection.id);
                                  setDriveFolderModalOpen(true);
                                }}
                                disabled={isLoadingAction}
                                className="inline-flex items-center gap-1.5 h-8 px-3 border border-gray-200 rounded-lg bg-white text-gray-600 text-xs font-medium hover:bg-gray-50 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-300"
                              >
                                <span>{t('integrations.createDriveFolder')}</span>
                              </button>
                            )}
                            <button
                              onClick={() => syncMutation.mutate(connection.id)}
                              disabled={isLoadingAction}
                              className="inline-flex items-center gap-1.5 h-8 px-3 border border-gray-200 rounded-lg bg-white text-gray-600 text-xs font-medium hover:bg-gray-50 hover:border-gray-300 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-300 dark:hover:bg-slate-700 transition-colors disabled:opacity-50"
                            >
                              <RefreshCw className={`w-3.5 h-3.5 ${syncMutation.isPending && syncMutation.variables === connection.id ? 'animate-spin' : ''}`} />
                              <span>{t('toolbar.sync')}</span>
                            </button>
                          </>
                        )}
                        {(isActive || isConnectionError) && (
                          <button
                            onClick={() => disconnectMutation.mutate(connection.id)}
                            disabled={isLoadingAction}
                            className="inline-flex items-center gap-1.5 h-8 px-3 border border-gray-200 rounded-lg bg-white text-gray-600 text-xs font-medium hover:border-red-500 hover:text-red-600 hover:bg-red-50 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-300 dark:hover:bg-red-500/10 dark:hover:text-red-400 dark:hover:border-red-500/50 transition-colors disabled:opacity-50"
                          >
                            {t('integrations.disconnect')}
                          </button>
                        )}
                        {isConnectionError && (
                          <button
                            onClick={() => handleConnect(service.integrationKey, service.serviceType)}
                            disabled={isLoadingAction}
                            className="inline-flex items-center gap-1.5 h-8 px-3 border border-transparent rounded-lg bg-brand-600 text-white text-xs font-medium hover:bg-brand-700 transition-colors disabled:opacity-50"
                          >
                            {t('integrations.reconnect')}
                          </button>
                        )}
                      </>
                    ) : (
                      <button
                        onClick={() => handleConnect(service.integrationKey, service.serviceType)}
                        disabled={isLoadingAction}
                        className="inline-flex items-center gap-1.5 h-8 px-3 border border-transparent rounded-lg bg-brand-600 text-white text-xs font-medium hover:bg-brand-700 transition-colors disabled:opacity-50"
                      >
                        <Plus className="w-3.5 h-3.5" />
                        <span>{t('integrations.connect')}</span>
                      </button>
                    )}
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}

      <CreateDriveFolderModal
        isOpen={driveFolderModalOpen}
        onClose={() => setDriveFolderModalOpen(false)}
        defaultConnectionId={driveFolderConnId}
      />
    </div>
  );
};

