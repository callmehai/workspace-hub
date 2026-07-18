import {
  type InfiniteData,
  type QueryClient,
} from '@tanstack/react-query';
import type { NavigateFunction } from 'react-router-dom';
import { showNotificationToast } from '../../components/NotificationToast';
import { formatNotificationDisplay } from '../../lib/notificationDisplay';
import type { ODataResponse } from '../../lib/odata';
import type {
  NotificationDto,
  NotificationType,
} from '../../types/notifications';

export const UNREAD_COUNT_KEY = [
  'notifications',
  'unread-count',
] as const;

export const NOTIFICATIONS_LIST_KEY = [
  'notifications',
  'list',
] as const;

const ITEMS_QUERY_KEY = ['items'] as const;

const INBOX_REFRESH_NOTIFICATION_TYPES: ReadonlySet<NotificationType> = new Set([
  'ItemSynced',
  'ImportantEmail',
]);

type NotificationsCache = InfiniteData<ODataResponse<NotificationDto>>;

function containsNotification(
  cached: NotificationsCache,
  notificationId: NotificationDto['id'],
): boolean {
  return cached.pages.some((page) =>
    page.value.some((item) => item.id === notificationId),
  );
}

function prependNotification(
  cached: NotificationsCache,
  notification: NotificationDto,
): NotificationsCache {
  const firstPage = cached.pages[0];

  if (!firstPage || containsNotification(cached, notification.id)) {
    return cached;
  }

  return {
    ...cached,
    pages: [
      {
        ...firstPage,
        value: [notification, ...firstPage.value],
        '@odata.count':
          (firstPage['@odata.count'] ?? firstPage.value.length) + 1,
      },
      ...cached.pages.slice(1),
    ],
  };
}

/** Cập nhật React Query cache và hiển thị toast khi SignalR đẩy notification mới. */
export function applyIncomingNotification(
  queryClient: QueryClient,
  navigate: NavigateFunction,
  notification: NotificationDto,
): void {
  const cachedList = queryClient.getQueryData<NotificationsCache>(
    NOTIFICATIONS_LIST_KEY,
  );

  // SignalR có thể gửi trùng sau reconnect. Không tăng unread count lần nữa.
  if (cachedList && containsNotification(cachedList, notification.id)) {
    return;
  }

  queryClient.setQueryData<number>(UNREAD_COUNT_KEY, (previous) =>
    (previous ?? 0) + 1,
  );

  if (!cachedList?.pages.length) {
    void queryClient.invalidateQueries({
      queryKey: NOTIFICATIONS_LIST_KEY,
    });
  } else {
    queryClient.setQueryData<NotificationsCache>(
      NOTIFICATIONS_LIST_KEY,
      (previous) =>
        previous ? prependNotification(previous, notification) : previous,
    );
  }

  if (INBOX_REFRESH_NOTIFICATION_TYPES.has(notification.type)) {
    void queryClient.invalidateQueries({ queryKey: ITEMS_QUERY_KEY });
  }

  const { title, subtitle } = formatNotificationDisplay(notification);

  showNotificationToast(title, subtitle, () => {
    if (notification.linkUrl) {
      navigate(notification.linkUrl);
    }
  });
}
