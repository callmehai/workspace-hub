import { useEffect, useRef } from 'react';
import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr';
import { type InfiniteData, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { showNotificationToast } from '../components/NotificationToast';
import { useAuth } from './useAuth';
import type { NotificationDto } from '../types/notifications';
import type { PaginatedResponse } from '../lib/scheduledEmailsApi';

export const UNREAD_COUNT_KEY = ['notifications', 'unread-count'] as const;
export const NOTIFICATIONS_LIST_KEY = ['notifications', 'list'] as const;
const CSRF_COOKIE = 'wh_csrf';
const CSRF_HEADER = 'X-CSRF-Token';

type NotificationHandler = (notification: NotificationDto) => void;

// Singleton hub — handler SignalR gắn 1 lần/connection; subscriber add/remove qua effect (StrictMode-safe).
let hubConnection: HubConnection | null = null;
let hubOwnerUserId: string | null = null;
let hubStartPromise: Promise<void> | null = null;
let hubListenerAttached = false;
const subscribers = new Set<NotificationHandler>();

/** Dev-only: đọc trạng thái hub (Connected / Disconnected / …). */
export function getNotificationHubState(): string {
  return hubConnection?.state ?? 'None';
}

if (import.meta.env.DEV) {
  (globalThis as typeof globalThis & { __notificationHub?: { getState: typeof getNotificationHubState } })
    .__notificationHub = { getState: getNotificationHubState };
}

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

function dispatchNotification(notification: NotificationDto) {
  subscribers.forEach((handler) => handler(notification));
}

function attachHubListener(connection: HubConnection) {
  if (hubListenerAttached) return;
  connection.on('ReceiveNotification', dispatchNotification);
  hubListenerAttached = true;
}

function buildHubConnection(): HubConnection {
  hubListenerAttached = false;
  const csrf = readCookie(CSRF_COOKIE);
  const connection = new HubConnectionBuilder()
    .withUrl(getHubUrl(), {
      withCredentials: true,
      headers: csrf ? { [CSRF_HEADER]: csrf } : {},
    })
    // BE restart / 502 proxy → retry chậm dần, tránh spam console.
    .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
    .configureLogging(LogLevel.Error)
    .build();
  attachHubListener(connection);
  connection.onreconnected(() => {
    if (import.meta.env.DEV) {
      console.info('[NotificationHub] Reconnected');
    }
  });
  return connection;
}

async function stopHub(): Promise<void> {
  hubStartPromise = null;
  hubListenerAttached = false;
  const conn = hubConnection;
  hubConnection = null;
  hubOwnerUserId = null;
  if (conn) {
    await conn.stop().catch(() => undefined);
  }
}

function getHubForUser(userId: string): HubConnection {
  if (hubConnection && hubOwnerUserId === userId) {
    return hubConnection;
  }

  if (hubConnection) {
    void hubConnection.stop().catch(() => undefined);
  }

  hubStartPromise = null;
  hubConnection = buildHubConnection();
  hubOwnerUserId = userId;
  return hubConnection;
}

async function ensureHubStarted(connection: HubConnection): Promise<void> {
  if (connection.state === HubConnectionState.Connected) {
    return;
  }

  if (connection.state === HubConnectionState.Connecting && hubStartPromise) {
    return hubStartPromise;
  }

  hubStartPromise = connection.start().finally(() => {
    hubStartPromise = null;
  });
  return hubStartPromise;
}

/** Cập nhật cache + toast khi có notification mới (SignalR hoặc poll). */
export function applyIncomingNotification(
  queryClient: ReturnType<typeof useQueryClient>,
  navigate: ReturnType<typeof useNavigate>,
  notification: NotificationDto,
) {
  queryClient.setQueryData<number>(UNREAD_COUNT_KEY, (old) => (old ?? 0) + 1);

  queryClient.setQueryData<InfiniteData<PaginatedResponse<NotificationDto>>>(
    NOTIFICATIONS_LIST_KEY,
    (old) => {
      if (!old?.pages?.length) return old;
      const first = old.pages[0];
      if (first.value.some((n) => n.id === notification.id)) return old;
      return {
        ...old,
        pages: [
          {
            ...first,
            value: [notification, ...first.value],
            '@odata.count': (first['@odata.count'] ?? first.value.length) + 1,
          },
          ...old.pages.slice(1),
        ],
      };
    },
  );

  queryClient.invalidateQueries({ queryKey: ['items'] });

  showNotificationToast(notification.title, notification.body, () => {
    if (notification.linkUrl) navigate(notification.linkUrl);
  });
}

export function useNotificationHub() {
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const queryClientRef = useRef(queryClient);
  const navigateRef = useRef(navigate);
  queryClientRef.current = queryClient;
  navigateRef.current = navigate;

  const userId = user?.id;

  useEffect(() => {
    if (!userId) {
      void stopHub();
      return;
    }

    const connection = getHubForUser(userId);

    const handler: NotificationHandler = (notification) => {
      applyIncomingNotification(queryClientRef.current, navigateRef.current, notification);
    };

    subscribers.add(handler);

    let cancelled = false;
    void ensureHubStarted(connection)
      .then(() => {
        if (!cancelled && import.meta.env.DEV) {
          console.info('[NotificationHub] Connected');
        }
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          console.warn('[NotificationHub] Connect failed:', err);
        }
      });

    return () => {
      cancelled = true;
      subscribers.delete(handler);
    };
  }, [userId]);
}
