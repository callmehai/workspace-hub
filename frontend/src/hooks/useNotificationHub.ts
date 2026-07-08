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
import { formatNotificationDisplay } from '../lib/notificationDisplay';
import type { ODataResponse } from '../lib/odata';
import { useAuth } from './useAuth';
import type { NotificationDto, NotificationType } from '../types/notifications';

export const UNREAD_COUNT_KEY = ['notifications', 'unread-count'] as const;
export const NOTIFICATIONS_LIST_KEY = ['notifications', 'list'] as const;

const CSRF_COOKIE = 'wh_csrf';
const CSRF_HEADER = 'X-CSRF-Token';

/** Backoff cho withAutomaticReconnect và manual start() retry. */
const RECONNECT_DELAYS_MS = [0, 2_000, 5_000, 10_000, 30_000] as const;

const INBOX_REFRESH_NOTIFICATION_TYPES: ReadonlySet<NotificationType> = new Set([
  'ItemSynced',
  'ImportantEmail',
]);

type NotificationHandler = (notification: NotificationDto) => void;

class HubConnectCancelled extends Error {
  override name = 'HubConnectCancelled';
}

let activeConnection: HubConnection | null = null;
let activeUserId: string | null = null;
let connectPromise: Promise<void> | null = null;
let connectAbortHandle: { userId: string; aborted: boolean } | null = null;
let receiveHandlerRegistered = false;
const subscribers = new Set<NotificationHandler>();

export function getNotificationHubState(): string {
  return activeConnection?.state ?? 'None';
}

if (import.meta.env.DEV) {
  (
    globalThis as typeof globalThis & { __notificationHub?: { getState: typeof getNotificationHubState } }
  ).__notificationHub = { getState: getNotificationHubState };
}

function resolveHubUrl(): string {
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

function isHubConnected(connection: HubConnection): boolean {
  return connection.state === HubConnectionState.Connected;
}

function isHubConnectedForUser(userId: string): boolean {
  return activeUserId === userId && activeConnection !== null && isHubConnected(activeConnection);
}

function notifySubscribers(notification: NotificationDto): void {
  for (const handler of subscribers) {
    try {
      handler(notification);
    } catch (error) {
      if (import.meta.env.DEV) {
        console.warn('[NotificationHub] Subscriber threw:', error);
      }
    }
  }
}

function registerReceiveHandler(connection: HubConnection): void {
  if (receiveHandlerRegistered) return;
  connection.on('ReceiveNotification', notifySubscribers);
  receiveHandlerRegistered = true;
}

function buildHubConnection(): HubConnection {
  receiveHandlerRegistered = false;
  const csrfToken = readCookie(CSRF_COOKIE);

  const connection = new HubConnectionBuilder()
    .withUrl(resolveHubUrl(), {
      withCredentials: true,
      headers: csrfToken ? { [CSRF_HEADER]: csrfToken } : {},
    })
    .withAutomaticReconnect([...RECONNECT_DELAYS_MS])
    .configureLogging(LogLevel.Error)
    .build();

  registerReceiveHandler(connection);

  connection.onreconnected(() => {
    if (import.meta.env.DEV) {
      console.info('[NotificationHub] Reconnected (automatic)');
    }
  });

  // onclose chỉ fire sau khi withAutomaticReconnect hết lượt — fallback manual connect.
  connection.onclose((error) => {
    if (import.meta.env.DEV && error) {
      console.warn('[NotificationHub] Connection closed after reconnect exhausted:', error);
    }

    const ownerUserId = activeUserId;
    if (ownerUserId && activeConnection === connection && !connectAbortHandle?.aborted) {
      void ensureHubStarted(ownerUserId).catch(() => undefined);
    }
  });

  return connection;
}

function ensureHubForUser(userId: string): HubConnection {
  if (activeConnection && activeUserId === userId) {
    return activeConnection;
  }

  if (activeConnection) {
    abortHubConnect(activeUserId ?? undefined);
    void activeConnection.stop().catch(() => undefined);
  }

  activeConnection = buildHubConnection();
  activeUserId = userId;
  return activeConnection;
}

function replaceHubForUser(userId: string): HubConnection {
  if (activeConnection) {
    void activeConnection.stop().catch(() => undefined);
  }
  connectPromise = null;
  activeConnection = buildHubConnection();
  activeUserId = userId;
  return activeConnection;
}

function waitBeforeRetry(delayMs: number, shouldAbort: () => boolean): Promise<void> {
  return new Promise((resolve, reject) => {
    if (shouldAbort()) {
      reject(new HubConnectCancelled());
      return;
    }

    let settled = false;
    const finish = (next: () => void) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      document.removeEventListener('visibilitychange', onTabVisible);
      window.removeEventListener('online', onNetworkOnline);
      next();
    };

    const timer = setTimeout(() => finish(resolve), delayMs);

    const onTabVisible = () => {
      if (document.visibilityState === 'visible') {
        finish(resolve);
      }
    };

    const onNetworkOnline = () => finish(resolve);

    document.addEventListener('visibilitychange', onTabVisible);
    window.addEventListener('online', onNetworkOnline);
  });
}

function abortHubConnect(userId?: string): void {
  if (!connectAbortHandle) return;
  if (!userId || connectAbortHandle.userId === userId) {
    connectAbortHandle.aborted = true;
  }
}

async function startHubWithRetry(userId: string): Promise<void> {
  abortHubConnect(userId);

  const abortHandle = { userId, aborted: false };
  connectAbortHandle = abortHandle;

  const shouldAbort = () => abortHandle.aborted || activeUserId !== userId;
  let failedAttempts = 0;

  while (!shouldAbort()) {
    const connection =
      failedAttempts === 0 ? ensureHubForUser(userId) : replaceHubForUser(userId);

    try {
      await connection.start();
      if (import.meta.env.DEV) {
        console.info(
          failedAttempts > 0
            ? `[NotificationHub] Connected after ${failedAttempts} manual retry(s)`
            : '[NotificationHub] Connected',
        );
      }
      return;
    } catch (error) {
      if (shouldAbort()) {
        throw new HubConnectCancelled();
      }

      const delayMs =
        RECONNECT_DELAYS_MS[Math.min(failedAttempts, RECONNECT_DELAYS_MS.length - 1)];
      failedAttempts += 1;

      if (import.meta.env.DEV) {
        console.warn(
          `[NotificationHub] start() failed (attempt ${failedAttempts}), retry in ${delayMs}ms`,
          error,
        );
      }

      try {
        await waitBeforeRetry(delayMs, shouldAbort);
      } catch {
        throw new HubConnectCancelled();
      }
    }
  }

  throw new HubConnectCancelled();
}

async function stopHub(): Promise<void> {
  abortHubConnect();
  connectPromise = null;
  receiveHandlerRegistered = false;

  const connection = activeConnection;
  activeConnection = null;
  activeUserId = null;

  if (connection) {
    await connection.stop().catch(() => undefined);
  }
}

async function ensureHubStarted(userId: string): Promise<void> {
  if (isHubConnectedForUser(userId)) {
    return;
  }

  if (connectPromise) {
    return connectPromise;
  }

  connectPromise = startHubWithRetry(userId).finally(() => {
    connectPromise = null;
    if (connectAbortHandle?.userId === userId) {
      connectAbortHandle = null;
    }
  });

  return connectPromise;
}

function prependToNotificationList(
  cached: InfiniteData<ODataResponse<NotificationDto>>,
  notification: NotificationDto,
): InfiniteData<ODataResponse<NotificationDto>> {
  const firstPage = cached.pages[0];
  if (firstPage.value.some((item) => item.id === notification.id)) {
    return cached;
  }

  return {
    ...cached,
    pages: [
      {
        ...firstPage,
        value: [notification, ...firstPage.value],
        '@odata.count': (firstPage['@odata.count'] ?? firstPage.value.length) + 1,
      },
      ...cached.pages.slice(1),
    ],
  };
}

/** Cập nhật cache + toast khi có notification mới (SignalR). */
export function applyIncomingNotification(
  queryClient: ReturnType<typeof useQueryClient>,
  navigate: ReturnType<typeof useNavigate>,
  notification: NotificationDto,
): void {
  queryClient.setQueryData<number>(UNREAD_COUNT_KEY, (previous) => (previous ?? 0) + 1);

  const cachedList = queryClient.getQueryData<InfiniteData<ODataResponse<NotificationDto>>>(
    NOTIFICATIONS_LIST_KEY,
  );

  if (!cachedList?.pages?.length) {
    void queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_LIST_KEY });
  } else {
    queryClient.setQueryData<InfiniteData<ODataResponse<NotificationDto>>>(
      NOTIFICATIONS_LIST_KEY,
      (previous) => (previous ? prependToNotificationList(previous, notification) : previous),
    );
  }

  if (INBOX_REFRESH_NOTIFICATION_TYPES.has(notification.type)) {
    void queryClient.invalidateQueries({ queryKey: ['items'] });
  }

  const { title, subtitle } = formatNotificationDisplay(notification);
  showNotificationToast(title, subtitle, () => {
    if (notification.linkUrl) navigate(notification.linkUrl);
  });
}

export function useNotificationHub(): void {
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

    const handler: NotificationHandler = (notification) => {
      applyIncomingNotification(queryClientRef.current, navigateRef.current, notification);
    };

    subscribers.add(handler);

    let disposed = false;

    const connectIfNeeded = () => {
      if (disposed || isHubConnectedForUser(userId)) {
        return;
      }

      void ensureHubStarted(userId).catch((error: unknown) => {
        if (disposed || error instanceof HubConnectCancelled) return;
        if (import.meta.env.DEV) {
          console.warn('[NotificationHub] Connect ended:', error);
        }
      });
    };

    connectIfNeeded();

    const onTabVisible = () => {
      if (document.visibilityState === 'visible') {
        connectIfNeeded();
      }
    };

    document.addEventListener('visibilitychange', onTabVisible);

    return () => {
      disposed = true;
      abortHubConnect(userId);
      subscribers.delete(handler);
      document.removeEventListener('visibilitychange', onTabVisible);
    };
  }, [userId]);
}
