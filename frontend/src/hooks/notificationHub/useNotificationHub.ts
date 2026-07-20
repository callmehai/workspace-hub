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

export function useNotificationHub(): void {
  const userId = useAuth().user?.id;
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const queryClientRef = useRef(queryClient);
  const navigateRef = useRef(navigate);

  useEffect(() => {
    queryClientRef.current = queryClient;
    navigateRef.current = navigate;
  }, [queryClient, navigate]);

  useEffect(() => {
    if (!userId) {
      void notificationHubManager.disconnect();
      return;
    }

    let disposed = false;
    const unsubscribe = notificationHubManager.subscribe((notification) => {
      applyIncomingNotification(queryClientRef.current, navigateRef.current, notification);
    });

    let connectTimer: number | undefined;

    const connectIfNeeded = () => {
      if (disposed) return;

      void notificationHubManager.connect(userId).catch((error: unknown) => {
        if (!disposed && import.meta.env.DEV) {
          console.warn('[NotificationHub] Connect failed:', error);
        }
      });
    };

    const scheduleConnect = () => {
      if (connectTimer !== undefined) window.clearTimeout(connectTimer);
      connectTimer = window.setTimeout(() => {
        connectTimer = undefined;
        connectIfNeeded();
      }, 0);
    };

    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible') scheduleConnect();
    };

    scheduleConnect();
    window.addEventListener('online', scheduleConnect);
    document.addEventListener('visibilitychange', handleVisibilityChange);

    return () => {
      disposed = true;
      if (connectTimer !== undefined) window.clearTimeout(connectTimer);
      unsubscribe();
      window.removeEventListener('online', scheduleConnect);
      document.removeEventListener('visibilitychange', handleVisibilityChange);

      if (!notificationHubManager.hasSubscribers()) {
        void notificationHubManager.disconnect();
      }
    };
  }, [userId]);
}
