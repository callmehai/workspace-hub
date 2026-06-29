import { useEffect } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useMutation } from '@tanstack/react-query';
import { connectionsApi } from '../../lib/connectionsApi';
import { Loader2 } from 'lucide-react';
import toast from 'react-hot-toast';
import axios from 'axios';

export const OAuthCallback = () => {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();

  const code = searchParams.get('code');
  const state = searchParams.get('state');

  const callbackMutation = useMutation({
    mutationFn: connectionsApi.oauthCallback,
    onSuccess: () => {
      toast.success('Connection successfully established');
      navigate('/settings/integrations');
    },
    onError: (err) => {
      const message = axios.isAxiosError(err)
        ? err.response?.data?.message || 'Failed to establish connection'
        : 'Failed to establish connection';
      toast.error(message);
    },
  });

  useEffect(() => {
    if (!code || !state) {
      toast.error('Invalid callback parameters');
      return;
    }
    callbackMutation.mutate({ code, state });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const isError = callbackMutation.isError || (!code || !state);

  return (
    <div className="flex h-screen w-full items-center justify-center bg-gray-50">
      {isError ? (
        <div className="flex flex-col items-center space-y-4">
          <p className="text-red-500 font-medium">Failed to connect.</p>
          <button
            onClick={() => navigate('/settings/integrations')}
            className="px-4 py-2 bg-brand-600 text-white rounded-md hover:bg-brand-700 transition-colors"
          >
            Return to Integrations
          </button>
        </div>
      ) : (
        <div className="flex flex-col items-center space-y-4">
          <Loader2 className="h-8 w-8 animate-spin text-brand-600" />
          <p className="text-gray-600 font-medium">Connecting to service...</p>
        </div>
      )}
    </div>
  );
};
