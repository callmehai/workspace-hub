import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr';
import type { NotificationDto } from '../../types/notifications';

const HUB_EVENT = 'ReceiveNotification';
const CSRF_COOKIE = 'wh_csrf';
const CSRF_HEADER = 'X-CSRF-Token';

/**
 * withAutomaticReconnect dùng dãy delay này sau khi connection đang chạy bị mất.
 * startWithRetry cũng dùng nó khi lần connection.start() ban đầu thất bại.
 */
const RECONNECT_DELAYS_MS = [0, 2_000, 5_000, 10_000, 30_000] as const;

type NotificationHandler = (notification: NotificationDto) => void;

interface HubSession {
  readonly id: number;
  readonly userId: string;
  readonly abortController: AbortController;
  connection: HubConnection | null;
  startPromise: Promise<void> | null;
}

function resolveHubUrl(): string {
  const apiUrl = import.meta.env.VITE_API_URL;

  // Dùng reverse proxy /api ở cả Vite dev và production.
  if (!apiUrl || apiUrl.startsWith('/')) {
    return '/api/hubs/notifications';
  }

  const baseUrl = apiUrl.replace(/\/api\/?$/, '');
  return `${baseUrl}/api/hubs/notifications`;
}

function readCookie(name: string): string | null {
  const escapedName = name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = document.cookie.match(new RegExp(`(?:^|; )${escapedName}=([^;]*)`));

  return match ? decodeURIComponent(match[1]) : null;
}

async function detachConnection(connection: HubConnection | null): Promise<void> {
  if (!connection) return;

  // Không nhận thêm notification từ connection cũ trong lúc stop.
  connection.off(HUB_EVENT);

  if (connection.state === HubConnectionState.Disconnected) {
    return;
  }

  try {
    await connection.stop();
  } catch (error) {
    if (import.meta.env.DEV) {
      console.warn('[NotificationHub] Could not stop connection:', error);
    }
  }
}

/**
 * Chờ đến lần retry tiếp theo, nhưng retry ngay khi:
 * - browser online trở lại;
 * - người dùng quay lại tab;
 * - session bị abort.
 */
function waitBeforeRetry(delayMs: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    if (signal.aborted) {
      resolve();
      return;
    }

    let settled = false;
    let timerId: number | undefined;

    const cleanup = () => {
      if (timerId !== undefined) {
        window.clearTimeout(timerId);
      }

      signal.removeEventListener('abort', finish);
      window.removeEventListener('online', finish);
      document.removeEventListener('visibilitychange', onVisibilityChange);
    };

    const finish = () => {
      if (settled) return;

      settled = true;
      cleanup();
      resolve();
    };

    const onVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        finish();
      }
    };

    timerId = window.setTimeout(finish, delayMs);

    signal.addEventListener('abort', finish, { once: true });
    window.addEventListener('online', finish, { once: true });
    document.addEventListener('visibilitychange', onVisibilityChange);
  });
}

class NotificationHubManager {
  private sessionSequence = 0;
  private operationSequence = 0;
  private activeSession: HubSession | null = null;
  private stopPromise: Promise<void> = Promise.resolve();
  private readonly subscribers = new Set<NotificationHandler>();

  subscribe(handler: NotificationHandler): () => void {
    this.subscribers.add(handler);

    return () => {
      this.subscribers.delete(handler);
    };
  }

  hasSubscribers(): boolean {
    return this.subscribers.size > 0;
  }

  getState(): string {
    return this.activeSession?.connection?.state ?? 'None';
  }

  connect(userId: string): Promise<void> {
    const currentSession = this.activeSession;

    if (currentSession?.userId === userId) {
      if (currentSession.startPromise) {
        return currentSession.startPromise;
      }

      const state = currentSession.connection?.state;

      if (
        state === HubConnectionState.Connected ||
        state === HubConnectionState.Connecting ||
        state === HubConnectionState.Reconnecting
      ) {
        return Promise.resolve();
      }

      return this.startSession(currentSession);
    }

    const operationId = ++this.operationSequence;
    return this.replaceSession(userId, operationId);
  }

  async disconnect(): Promise<void> {
    // Làm mọi connect() đang chờ trở thành stale.
    ++this.operationSequence;
    await this.stopCurrentSession();
  }

  private async replaceSession(userId: string, operationId: number): Promise<void> {
    await this.stopCurrentSession();

    // Trong lúc await stop, có thể đã có connect/disconnect mới hơn.
    if (operationId !== this.operationSequence) {
      return;
    }

    const session: HubSession = {
      id: ++this.sessionSequence,
      userId,
      abortController: new AbortController(),
      connection: null,
      startPromise: null,
    };

    this.activeSession = session;
    await this.startSession(session);
  }

  private async stopCurrentSession(): Promise<void> {
    const session = this.activeSession;

    if (!session) {
      await this.stopPromise.catch(() => undefined);
      return;
    }

    // Clear sync trước mọi await để connection cũ không còn được xem là active.
    this.activeSession = null;
    session.abortController.abort();

    const previousStop = this.stopPromise;
    const stopping = previousStop
      .catch(() => undefined)
      .then(async () => {
        await Promise.all([
          session.startPromise?.catch(() => undefined) ?? Promise.resolve(),
          detachConnection(session.connection),
        ]);

        session.connection = null;
      });

    this.stopPromise = stopping;
    await stopping;

    if (this.stopPromise === stopping) {
      this.stopPromise = Promise.resolve();
    }
  }

  private startSession(session: HubSession): Promise<void> {
    if (!this.isActive(session)) {
      return Promise.resolve();
    }

    if (session.startPromise) {
      return session.startPromise;
    }

    const startPromise = this.startWithRetry(session).finally(() => {
      if (session.startPromise === startPromise) {
        session.startPromise = null;
      }
    });

    session.startPromise = startPromise;
    return startPromise;
  }

  private async startWithRetry(session: HubSession): Promise<void> {
    let failedAttempts = 0;

    while (this.isActive(session)) {
      // Mỗi attempt dùng connection mới để đọc lại cookie/JWT mới nhất.
      await detachConnection(session.connection);
      session.connection = null;

      if (!this.isActive(session)) {
        return;
      }

      const connection = this.buildConnection(session);
      session.connection = connection;

      try {
        await connection.start();

        if (!this.isActive(session) || session.connection !== connection) {
          await detachConnection(connection);
          return;
        }

        if (import.meta.env.DEV) {
          console.info(
            failedAttempts === 0
              ? '[NotificationHub] Connected'
              : `[NotificationHub] Connected after ${failedAttempts} manual retry(s)`,
          );
        }

        return;
      } catch (error) {
        await detachConnection(connection);

        if (session.connection === connection) {
          session.connection = null;
        }

        if (!this.isActive(session)) {
          return;
        }

        const delayMs =
          RECONNECT_DELAYS_MS[
            Math.min(failedAttempts, RECONNECT_DELAYS_MS.length - 1)
          ];

        failedAttempts += 1;

        if (import.meta.env.DEV) {
          console.warn(
            `[NotificationHub] start() failed (attempt ${failedAttempts}), retry in ${delayMs}ms`,
            error,
          );
        }

        await waitBeforeRetry(delayMs, session.abortController.signal);
      }
    }
  }

  private buildConnection(session: HubSession): HubConnection {
    const csrfToken = readCookie(CSRF_COOKIE);

    const connection = new HubConnectionBuilder()
      .withUrl(resolveHubUrl(), {
        withCredentials: true,
        headers: csrfToken ? { [CSRF_HEADER]: csrfToken } : {},
      })
      .withAutomaticReconnect([...RECONNECT_DELAYS_MS])
      .configureLogging(LogLevel.Error)
      .build();

    connection.on(HUB_EVENT, (notification: NotificationDto) => {
      // Chặn notification đến muộn từ connection của session/user cũ.
      if (!this.isActive(session) || session.connection !== connection) {
        return;
      }

      this.notifySubscribers(notification);
    });

    connection.onreconnecting((error) => {
      if (import.meta.env.DEV && this.isActive(session)) {
        console.warn('[NotificationHub] Reconnecting...', error);
      }
    });

    connection.onreconnected(() => {
      if (import.meta.env.DEV && this.isActive(session)) {
        console.info('[NotificationHub] Reconnected automatically');
      }
    });

    // Chỉ chạy sau khi withAutomaticReconnect đã dùng hết các lần retry.
    connection.onclose((error) => {
      if (!this.isActive(session) || session.connection !== connection) {
        return;
      }

      session.connection = null;

      if (import.meta.env.DEV && error) {
        console.warn(
          '[NotificationHub] Connection closed after automatic reconnect exhausted:',
          error,
        );
      }

      void this.startSession(session).catch((startError: unknown) => {
        if (import.meta.env.DEV && this.isActive(session)) {
          console.warn('[NotificationHub] Manual reconnect ended:', startError);
        }
      });
    });

    return connection;
  }

  private isActive(session: HubSession): boolean {
    return (
      this.activeSession === session &&
      !session.abortController.signal.aborted
    );
  }

  private notifySubscribers(notification: NotificationDto): void {
    for (const handler of this.subscribers) {
      try {
        handler(notification);
      } catch (error) {
        if (import.meta.env.DEV) {
          console.warn('[NotificationHub] Subscriber threw:', error);
        }
      }
    }
  }
}

export const notificationHubManager = new NotificationHubManager();

export function getNotificationHubState(): string {
  return notificationHubManager.getState();
}

if (import.meta.env.DEV) {
  (
    globalThis as typeof globalThis & {
      __notificationHub?: {
        getState: typeof getNotificationHubState;
      };
    }
  ).__notificationHub = {
    getState: getNotificationHubState,
  };
}
