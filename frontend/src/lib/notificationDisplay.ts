import type { NotificationDto } from '../types/notifications';
import { translate, type TranslationKey } from '../i18n/translations';

export interface NotificationPayload {
  from?: string;
  itemTitle?: string;
  preview?: string;
}

type TranslateFn = (key: TranslationKey, vars?: Record<string, string | number>) => string;

const NOTIFICATION_TITLE_PREFIX = 'notifications.';

export function parseNotificationPayload(body: string): NotificationPayload | null {
  try {
    const parsed: unknown = JSON.parse(body);
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return null;
    return parsed as NotificationPayload;
  } catch {
    return null;
  }
}

/** Dịch title theo lang hiện tại; body JSON chứa preview + biến interpolate (from, itemTitle). */
export function formatNotificationDisplay(
  notification: NotificationDto,
  t: TranslateFn = translate,
): { title: string; subtitle: string | null } {
  const payload = parseNotificationPayload(notification.body);

  if (notification.title.startsWith(NOTIFICATION_TITLE_PREFIX)) {
    const key = notification.title as TranslationKey;
    const vars: Record<string, string> = {};
    if (payload?.from) vars.from = payload.from;
    if (payload?.itemTitle) vars.itemTitle = payload.itemTitle;

    return {
      title: t(key, vars),
      subtitle: payload?.preview ?? null,
    };
  }

  // Legacy rows (plain English/Vietnamese title + text body)
  return {
    title: notification.title,
    subtitle: payload?.preview ?? (notification.body || null),
  };
}
