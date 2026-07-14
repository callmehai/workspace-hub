import { useEffect, useRef } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useMutation } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { connectionsApi } from '../../lib/connectionsApi';
import type { ApiErrorResponse } from '../../lib/errorUtils';
import type { TranslationKey } from '../../i18n/translations';
import { Loader2 } from 'lucide-react';
import toast from 'react-hot-toast';
import { useI18n } from '../../hooks/useI18n';

export const OAuthCallback = () => {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { t } = useI18n();

  const code = searchParams.get('code');
  const state = searchParams.get('state');

  const { mutate: triggerCallback, isError: isMutationError } = useMutation({
    mutationFn: connectionsApi.oauthCallback,
    onSuccess: () => {
      toast.success(t('oauth.connected'));
      navigate('/integrations');
    },
    onError: (err) => {
      console.error(err);
      const msg = isAxiosError(err)
        ? (err.response?.data as ApiErrorResponse | undefined)?.message?.trim()
        : undefined;
      if (msg?.startsWith('integrations.')) {
        toast.error(t(msg as TranslationKey, { name: t('integrations.title') }));
        return;
      }
      toast.error(msg || t('oauth.failed'));
    },
  });

  const called = useRef(false);

  useEffect(() => {
    if (called.current) return;
    called.current = true;

    if (!code || !state) {
      toast.error(t('oauth.failed'));
      return;
    }
    triggerCallback({ code, state });
  }, [code, state, triggerCallback, t]);

  const isError = isMutationError || (!code || !state);

  return (
    <div className="flex h-screen w-full items-center justify-center bg-gray-50 dark:bg-slate-950">
      {isError ? (
        <div className="flex flex-col items-center space-y-4">
          <p className="text-red-500 dark:text-red-400 font-medium">{t('oauth.failed')}</p>
          <button
            onClick={() => navigate('/integrations')}
            className="px-4 py-2 bg-brand-600 text-white rounded-md hover:bg-brand-700 transition-colors"
          >
            {t('oauth.backToIntegrations')}
          </button>
        </div>
      ) : (
        <div className="flex flex-col items-center space-y-4">
          <Loader2 className="h-8 w-8 animate-spin text-brand-600" />
          <p className="text-gray-600 dark:text-slate-300 font-medium">{t('oauth.connecting')}</p>
        </div>
      )}
    </div>
  );
};
