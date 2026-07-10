import { useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useAuth } from './useAuth';
import { usePollingInterval } from './usePollingInterval';

/** QueryKey prefix cần làm mới sau cron sync BE (~60s). Chỉ refetch query đang mount (TanStack Query). */
const SYNC_QUERY_PREFIXES = [
  ['items'],
  ['contacts'],
  ['contact'],
  ['connections'],
] as const;

/**
 * Poll toàn app (MainLayout): invalidate cache → trang đang mở tự refetch từ DB.
 * Tạm dừng khi tab hidden. Focus lại → refresh ngay.
 */
export function useBackgroundDataRefresh() {
  const queryClient = useQueryClient();
  const { user } = useAuth();
  const pollMs = usePollingInterval(60_000);

  useEffect(() => {
    if (!user) return;

    const refresh = () => {
      if (document.hidden) return;
      for (const key of SYNC_QUERY_PREFIXES) {
        void queryClient.refetchQueries({ queryKey: [...key], type: 'active' });
      }
    };

    const id = window.setInterval(refresh, pollMs);
    const onFocus = () => refresh();
    window.addEventListener('focus', onFocus);

    return () => {
      window.clearInterval(id);
      window.removeEventListener('focus', onFocus);
    };
  }, [user, queryClient, pollMs]);
}
