import { useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { connectionsApi } from '../../lib/connectionsApi';
import { Loader2 } from 'lucide-react';
import toast from 'react-hot-toast';

export const OAuthCallback = () => {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  
  const code = searchParams.get('code');
  const state = searchParams.get('state');
  const hasValidParams = !!(code && state);

  const [status, setStatus] = useState<'loading' | 'error'>(hasValidParams ? 'loading' : 'error');

  useEffect(() => {
    if (!hasValidParams) {
      toast.error('Invalid callback parameters');
      return;
    }

    const processCallback = async () => {
      try {
        await connectionsApi.oauthCallback({ code, state });
        toast.success('Connection successfully established');
        navigate('/settings/integrations');
      } catch (err) {
        const e = err as { response?: { data?: { message?: string } } };
        toast.error(e.response?.data?.message || 'Failed to establish connection');
        setStatus('error');
      }
    };

    processCallback();
  }, [code, state, hasValidParams, navigate]);

  return (
    <div className="flex h-screen w-full items-center justify-center bg-gray-50">
      {status === 'loading' ? (
        <div className="flex flex-col items-center space-y-4">
          <Loader2 className="h-8 w-8 animate-spin text-brand-600" />
          <p className="text-gray-600 font-medium">Connecting to service...</p>
        </div>
      ) : (
        <div className="flex flex-col items-center space-y-4">
          <p className="text-red-500 font-medium">Failed to connect.</p>
          <button
            onClick={() => navigate('/settings/integrations')}
            className="px-4 py-2 bg-brand-600 text-white rounded-md hover:bg-brand-700 transition-colors"
          >
            Return to Integrations
          </button>
        </div>
      )}
    </div>
  );
};
