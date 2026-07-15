import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../useAuth';
import { applyIncomingNotification } from './notificationCache';
import { notificationHubManager } from './notificationHubManager';

export {
  applyIncomingNotification,
  NOTIFICATIONS_LIST_KEY,
  UNREAD_COUNT_KEY,
} from './notificationCache';

export { getNotificationHubState } from './notificationHubManager';

/**
 * Hook này nên được mount một lần trong MainLayout/authenticated app shell.
 */
export function useNotificationHub(): void {
  const userId = useAuth().user?.id;
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  // Handler SignalR luôn dùng instance mới nhất mà không reconnect hub
  // chỉ vì navigate/queryClient đổi reference.
  const queryClientRef = useRef(queryClient);
  const navigateRef = useRef(navigate);

  queryClientRef.current = queryClient;
  navigateRef.current = navigate;

  useEffect(() => {
    if (!userId) {
      void notificationHubManager.disconnect();
      return;
    }

    let disposed = false;

    const unsubscribe = notificationHubManager.subscribe((notification) => {
      applyIncomingNotification(
        queryClientRef.current,
        navigateRef.current,
        notification,
      );
    });

    const connectIfNeeded = () => {
      if (disposed) return;

      void notificationHubManager.connect(userId).catch((error: unknown) => {
        if (disposed || !import.meta.env.DEV) return;

        console.warn('[NotificationHub] Connect ended:', error);
      });
    };

    const onVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        connectIfNeeded();
      }
    };

    connectIfNeeded();

    document.addEventListener('visibilitychange', onVisibilityChange);
    window.addEventListener('online', connectIfNeeded);

    return () => {
      disposed = true;
      unsubscribe();

      document.removeEventListener('visibilitychange', onVisibilityChange);
      window.removeEventListener('online', connectIfNeeded);

      // ProtectedRoute có thể unmount MainLayout ngay khi logout, trước khi
      // effect kịp chạy lại với userId = undefined. Stop ở cleanup để tránh
      // connection zombie và tránh reuse auth/cookie cũ ở lần login sau.
      if (!notificationHubManager.hasSubscribers()) {
        void notificationHubManager.disconnect();
      }
    };
  }, [userId]);
}
