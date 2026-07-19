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
const RETRY_DELAYS_MS = [0, 2_000, 5_000, 10_000, 30_000] as const;

type NotificationHandler = (notification: NotificationDto) => void;

function resolveHubUrl(): string {
  const apiUrl = import.meta.env.VITE_API_URL;
  if (!apiUrl || apiUrl.startsWith('/')) return '/api/hubs/notifications';

  const baseUrl = apiUrl.replace(/\/api\/?$/, '');
  return `${baseUrl}/api/hubs/notifications`;
}

function readCookie(name: string): string | null {
  const escapedName = name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = document.cookie.match(new RegExp(`(?:^|; )${escapedName}=([^;]*)`));
  return match ? decodeURIComponent(match[1]) : null;
}

function wait(delayMs: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, delayMs));
}

class NotificationHubManager {
  private connection: HubConnection | null = null;
  private currentUserId: string | null = null;
  private startPromise: Promise<void> | null = null;
  private lifecycleVersion = 0;
  private readonly subscribers = new Set<NotificationHandler>();

  subscribe(handler: NotificationHandler): () => void {
    this.subscribers.add(handler);
    return () => this.subscribers.delete(handler);
  }

  hasSubscribers(): boolean {
    return this.subscribers.size > 0;
  }

  getState(): string {
    return this.connection?.state ?? 'None';
  }

  async connect(userId: string): Promise<void> {
    if (this.currentUserId === userId && this.startPromise) return this.startPromise;

    if (
      this.currentUserId === userId &&
      this.connection &&
      this.connection.state !== HubConnectionState.Disconnected
    ) {
      return;
    }

    if (this.currentUserId && this.currentUserId !== userId) {
      await this.disconnect();
    }

    this.currentUserId = userId;
    const lifecycleVersion = ++this.lifecycleVersion;
    const startPromise = this.startWithRetry(userId, lifecycleVersion).finally(() => {
      if (this.startPromise === startPromise) this.startPromise = null;
    });

    this.startPromise = startPromise;
    return startPromise;
  }

  async disconnect(): Promise<void> {
    ++this.lifecycleVersion;

    const oldConnection = this.connection;
    this.connection = null;
    this.currentUserId = null;
    oldConnection?.off(HUB_EVENT);

    if (!oldConnection || oldConnection.state === HubConnectionState.Disconnected) return;

    try {
      await oldConnection.stop();
    } catch (error) {
      if (import.meta.env.DEV) console.warn('[NotificationHub] Stop failed:', error);
    }
  }

  private async startWithRetry(userId: string, lifecycleVersion: number): Promise<void> {
    let attempt = 0;

    while (this.isCurrentLifecycle(userId, lifecycleVersion)) {
      const connection = this.buildConnection(userId, lifecycleVersion);
      this.connection = connection;

      try {
        await connection.start();

        if (
          !this.isCurrentLifecycle(userId, lifecycleVersion) ||
          this.connection !== connection
        ) {
          await this.stopStaleConnection(connection);
          return;
        }

        if (import.meta.env.DEV) {
          console.info(
            attempt === 0
              ? '[NotificationHub] Connected'
              : `[NotificationHub] Connected after ${attempt} retry attempt(s)`,
          );
        }
        return;
      } catch (error) {
        if (this.connection === connection) this.connection = null;
        await this.stopStaleConnection(connection);
        if (!this.isCurrentLifecycle(userId, lifecycleVersion)) return;

        const delayMs = RETRY_DELAYS_MS[Math.min(attempt, RETRY_DELAYS_MS.length - 1)];
        attempt += 1;

        if (import.meta.env.DEV) {
          console.warn(`[NotificationHub] Start failed. Retry in ${delayMs}ms`, error);
        }

        await wait(delayMs);
      }
    }
  }

  private buildConnection(userId: string, lifecycleVersion: number): HubConnection {
    const csrfToken = readCookie(CSRF_COOKIE);
    const connection = new HubConnectionBuilder()
      .withUrl(resolveHubUrl(), {
        withCredentials: true,
        headers: csrfToken ? { [CSRF_HEADER]: csrfToken } : {},
      })
      .withAutomaticReconnect([...RETRY_DELAYS_MS])
      .configureLogging(LogLevel.Error)
      .build();

    connection.on(HUB_EVENT, (notification: NotificationDto) => {
      if (
        !this.isCurrentLifecycle(userId, lifecycleVersion) ||
        this.connection !== connection
      ) {
        return;
      }

      this.notifySubscribers(notification);
    });

    connection.onreconnecting((error) => {
      if (import.meta.env.DEV && this.isCurrentLifecycle(userId, lifecycleVersion)) {
        console.warn('[NotificationHub] Reconnecting...', error);
      }
    });

    connection.onreconnected(() => {
      if (import.meta.env.DEV && this.isCurrentLifecycle(userId, lifecycleVersion)) {
        console.info('[NotificationHub] Reconnected automatically');
      }
    });

    connection.onclose((error) => {
      if (
        !this.isCurrentLifecycle(userId, lifecycleVersion) ||
        this.connection !== connection
      ) {
        return;
      }

      this.connection = null;
      if (import.meta.env.DEV && error) {
        console.warn(
          '[NotificationHub] Connection closed after automatic reconnect exhausted:',
          error,
        );
      }

      void this.restartCurrentLifecycle(userId, lifecycleVersion);
    });

    return connection;
  }

  private async restartCurrentLifecycle(
    userId: string,
    lifecycleVersion: number,
  ): Promise<void> {
    if (!this.isCurrentLifecycle(userId, lifecycleVersion)) return;
    if (this.startPromise) return this.startPromise;

    const startPromise = this.startWithRetry(userId, lifecycleVersion).finally(() => {
      if (this.startPromise === startPromise) this.startPromise = null;
    });

    this.startPromise = startPromise;
    return startPromise;
  }

  private isCurrentLifecycle(userId: string, lifecycleVersion: number): boolean {
    return this.currentUserId === userId && this.lifecycleVersion === lifecycleVersion;
  }

  private async stopStaleConnection(connection: HubConnection): Promise<void> {
    connection.off(HUB_EVENT);
    if (connection.state === HubConnectionState.Disconnected) return;

    try {
      await connection.stop();
    } catch (error) {
      if (import.meta.env.DEV) {
        console.warn('[NotificationHub] Stop stale connection failed:', error);
      }
    }
  }

  private notifySubscribers(notification: NotificationDto): void {
    for (const handler of this.subscribers) {
      try {
        handler(notification);
      } catch (error) {
        if (import.meta.env.DEV) console.warn('[NotificationHub] Subscriber error:', error);
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
