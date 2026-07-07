import { useEffect, useRef } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useMutation } from '@tanstack/react-query';
import type { AxiosError } from 'axios';
import { connectionsApi } from '../../lib/connectionsApi';
import { Loader2 } from 'lucide-react';
import toast from 'react-hot-toast';

export const OAuthCallback = () => {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();

  const code = searchParams.get('code');
  const state = searchParams.get('state');

  const { mutate: triggerCallback, isError: isMutationError } = useMutation({
    mutationFn: connectionsApi.oauthCallback,
    onSuccess: () => {
      toast.success('Kết nối thành công');
      navigate('/integrations');
    },
    onError: (err) => {
      console.error(err);
      const message = (err as AxiosError<{ message?: string }>)?.response?.data?.message || 'Kết nối thất bại';
      toast.error(message);
    },
  });

  const called = useRef(false);

  useEffect(() => {
    if (called.current) return;
    called.current = true;

    if (!code || !state) {
      toast.error('Kết nối thất bại');
      return;
    }
    triggerCallback({ code, state });
  }, [code, state, triggerCallback]);

  const isError = isMutationError || (!code || !state);

  return (
    <div className="flex h-screen w-full items-center justify-center bg-gray-50 dark:bg-slate-950">
      {isError ? (
        <div className="flex flex-col items-center space-y-4">
          <p className="text-red-500 dark:text-red-400 font-medium">Kết nối thất bại.</p>
          <button
            onClick={() => navigate('/integrations')}
            className="px-4 py-2 bg-brand-600 text-white rounded-md hover:bg-brand-700 transition-colors"
          >
            Quay lại trang Tích hợp
          </button>
        </div>
      ) : (
        <div className="flex flex-col items-center space-y-4">
          <Loader2 className="h-8 w-8 animate-spin text-brand-600" />
          <p className="text-gray-600 dark:text-slate-300 font-medium">Đang thiết lập kết nối...</p>
        </div>
      )}
    </div>
  );
};
