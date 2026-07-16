import { useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import type { AxiosError } from 'axios';
import { isAxiosError } from 'axios';
import { connectionsApi, type OAuthCallbackResponse } from '../../lib/connectionsApi';
import type { ApiErrorResponse } from '../../lib/errorUtils';
import type { TranslationKey } from '../../i18n/translations';
import { Loader2 } from 'lucide-react';
import toast from 'react-hot-toast';
import { useI18n } from '../../hooks/useI18n';

// Module-level cache to ensure the API call is made exactly once per redirect,
// and to share the mutation state across double-mounts in React 18 Strict Mode.
let activePromise: Promise<OAuthCallbackResponse> | null = null;
let activeStatus: 'idle' | 'pending' | 'success' | 'error' = 'idle';
let activeErrorMsg: string | null = null;
let currentCode: string | null = null;
let currentState: string | null = null;

const listeners = new Set<() => void>();
const notifyListeners = () => listeners.forEach(l => l());

export const OAuthCallback = () => {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { t } = useI18n();

  const code = searchParams.get('code');
  const state = searchParams.get('state');

  const [, forceUpdate] = useState({});

  // Subscribe to changes in mutation status/errors
  useEffect(() => {
    const handler = () => forceUpdate({});
    listeners.add(handler);
    return () => {
      listeners.delete(handler);
    };
  }, []);

  useEffect(() => {
    // If search params change, reset the request state to trigger a new token exchange
    if (code !== currentCode || state !== currentState) {
      currentCode = code;
      currentState = state;
      activePromise = null;
      activeStatus = 'idle';
      activeErrorMsg = null;
      notifyListeners();
    }

    if (!code || !state) {
      activeStatus = 'error';
      activeErrorMsg = t('oauth.failed');
      notifyListeners();
      return;
    }

    if (activeStatus === 'idle' && !activePromise) {
      activeStatus = 'pending';
      notifyListeners();

      activePromise = connectionsApi.oauthCallback({ code, state })
        .then((response) => {
          activeStatus = 'success';
          notifyListeners();
          toast.success(t('oauth.connected'));
          // Connection mới → cache cũ (connections + metadata Jira) đã stale.
          // Không invalidate ở đây thì phải F5 mới thấy dự án/người phụ trách.
          queryClient.invalidateQueries({ queryKey: ['connections'] });
          queryClient.invalidateQueries({ queryKey: ['jira'] });
          // Connect KHÔNG sync → item của connection vừa disconnect đã bị xoá khỏi DB.
          // Phải bỏ cache items, không thì list hiện ticket ma tới khi F5.
          queryClient.invalidateQueries({ queryKey: ['items'] });
          navigate('/integrations');
          return response;
        })
        .catch((err) => {
          activeStatus = 'error';
          console.error(err);
          const msg = isAxiosError(err)
            ? (err.response?.data as ApiErrorResponse | undefined)?.message?.trim()
            : (err as AxiosError<{ message?: string }>)?.response?.data?.message?.trim();
          if (msg?.startsWith('integrations.')) {
            activeErrorMsg = t(msg as TranslationKey, { name: t('integrations.title') });
            notifyListeners();
            toast.error(activeErrorMsg);
            throw err;
          }
          const message = msg || t('oauth.failed');
          activeErrorMsg = message;
          notifyListeners();
          toast.error(message);
          throw err;
        });
    } else if (activeStatus === 'success') {
      navigate('/integrations');
    }
  }, [code, state, navigate, t, queryClient]);

  const isError = activeStatus === 'error';

  return (
    <div className="flex h-screen w-full items-center justify-center bg-gray-55 dark:bg-slate-950">
      {isError ? (
        <div className="flex flex-col items-center space-y-4 animate-in fade-in duration-200">
          <p className="text-red-500 dark:text-red-400 font-bold text-center px-6">
            {activeErrorMsg || t('oauth.failed')}
          </p>
          <button
            onClick={() => {
              // Reset state for future oauth runs
              activePromise = null;
              activeStatus = 'idle';
              activeErrorMsg = null;
              navigate('/integrations');
            }}
            className="px-4 py-2 bg-brand-600 text-white rounded-lg font-semibold hover:bg-brand-700 shadow-md transition-colors"
          >
            {t('oauth.backToIntegrations')}
          </button>
        </div>
      ) : (
        <div className="flex flex-col items-center space-y-4">
          <Loader2 className="h-8 w-8 animate-spin text-indigo-600 dark:text-indigo-400" />
          <p className="text-slate-600 dark:text-slate-300 font-semibold">{t('oauth.connecting')}</p>
        </div>
      )}
    </div>
  );
};
