import { useEffect, useRef } from 'react';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import { useAuth } from './useAuth';
import type { NotificationDto } from '../types/notifications';
import type { PaginatedResponse } from '../lib/scheduledEmailsApi';

const UNREAD_COUNT_KEY = ['notifications', 'unread-count'] as const;
const LIST_KEY_PREFIX = 'notifications';
const CSRF_COOKIE = 'wh_csrf';
const CSRF_HEADER = 'X-CSRF-Token';

function getHubUrl(): string {
  const apiUrl = import.meta.env.VITE_API_URL;
  if (!apiUrl || apiUrl.startsWith('/')) {
    return '/hubs/notifications';
  }
  const base = apiUrl.replace(/\/api\/?$/, '');
  return `${base}/hubs/notifications`;
}

function readCookie(name: string): string | null {
  const match = document.cookie.match(new RegExp(`(?:^|; )${name}=([^;]*)`));
  return match ? decodeURIComponent(match[1]) : null;
}

export function useNotificationHub() {
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const connectionRef = useRef<ReturnType<HubConnectionBuilder['build']> | null>(null);

  useEffect(() => {
    if (!user) {
      connectionRef.current?.stop();
      connectionRef.current = null;
      return;
    }

    // Negotiate là POST — CsrfMiddleware yêu cầu X-CSRF-Token khớp cookie wh_csrf.
    const csrf = readCookie(CSRF_COOKIE);
    const connection = new HubConnectionBuilder()
      .withUrl(getHubUrl(), {
        withCredentials: true,
        headers: csrf ? { [CSRF_HEADER]: csrf } : {},
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('ReceiveNotification', (notification: NotificationDto) => {
      queryClient.setQueryData<number>(UNREAD_COUNT_KEY, (old) => (old ?? 0) + 1);

      queryClient.setQueriesData<PaginatedResponse<NotificationDto>>(
        { queryKey: [LIST_KEY_PREFIX] },
        (old) => {
          if (!old?.value) return old;
          return {
            ...old,
            value: [notification, ...old.value],
            '@odata.count': (old['@odata.count'] ?? old.value.length) + 1,
          };
        },
      );

      toast.success(notification.title);
    });

    connectionRef.current = connection;
    connection.start().catch(() => {
      // Hub có thể chưa sẵn sàng khi dev — bỏ qua, reconnect tự xử lý
    });

    return () => {
      connection.stop();
      connectionRef.current = null;
    };
  }, [user]);
}

export { UNREAD_COUNT_KEY };
