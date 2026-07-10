import type { ItemResponse, ItemStatus } from '../types/items';
import type { TranslationKey } from '../i18n/translations';

/**
 * Email chưa đọc? Suy từ metadataJson (isUnread hoặc labels UNREAD) — KHÔNG có cột DB riêng (SCRUM-67).
 * Chỉ có nghĩa với Type=Email; loại khác luôn false (không áp style đã đọc/chưa đọc).
 */
export function isEmailUnread(item: ItemResponse): boolean {
  if (item.type !== 'Email' || !item.metadataJson) return false;
  try {
    const meta = JSON.parse(item.metadataJson);
    if (meta.isUnread !== undefined) return meta.isUnread === true;
    if (meta.IsUnread !== undefined) return meta.IsUnread === true;
    return Array.isArray(meta.labels) && meta.labels.includes('UNREAD');
  } catch {
    return false;
  }
}

// Key i18n cho 3 status Kanban (tái dùng nhãn cột kanban → list/board/drawer nhất quán).
const STATUS_KEY: Record<ItemStatus, TranslationKey> = {
  Inbox: 'kanban.colInbox', Doing: 'kanban.colDoing', Done: 'kanban.colDone',
};

function parseMeta(item: ItemResponse): Record<string, unknown> {
  if (!item.metadataJson) return {};
  try { return JSON.parse(item.metadataJson) as Record<string, unknown>; } catch { return {}; }
}

/**
 * Item "chưa xem" (để highlight kiểu Gmail đọc/chưa đọc):
 *  - Email  → nhãn read/unread THẬT của Gmail (isEmailUnread).
 *  - Event/File/Note/Ticket → provider không có nhãn read; app tự theo dõi qua seenStore
 *    (chưa mở detail trong app = chưa xem). `seen` là Set itemId đã xem (useSeenSet()).
 *
 * LƯU Ý: highlight chưa/đã xem là 1 TRỤC RIÊNG, độc lập với status chip. Ticket vẫn
 * hiển thị status THÔ từ Jira ở chip (getStatusLabel), chỉ thêm hiệu ứng highlight ở row.
 */
export function isItemUnread(item: ItemResponse, seen: Set<string>): boolean {
  if (item.type === 'Email') return isEmailUnread(item);
  return !seen.has(item.id);
}

/**
 * Status THÔ của Jira issue — giữ NGUYÊN tên lấy từ Jira (kể cả status tuỳ biến
 * như "In Review", "Blocked", "Backlog"...). Không map về Kanban. Null nếu không phải Ticket.
 */
export function getJiraStatus(item: ItemResponse): string | null {
  if (item.type !== 'Ticket') return null;
  const meta = parseMeta(item);
  const s = (meta.status ?? meta.Status);
  return typeof s === 'string' && s.trim() ? s.trim() : null;
}

/**
 * Nhãn trạng thái hiển thị của 1 item, xét theo LOẠI/integration (dịch qua i18n `t`):
 *  - Ticket (Jira): status THÔ từ Jira (data, giữ nguyên — không dịch, không ép Chưa xem/Đã xem).
 *  - Còn lại ở Inbox: Chưa xem | Đã xem theo `unread` (Email = Gmail; Event/File/Note = seenStore).
 *  - Doing/Done: theo cột Kanban.
 * `unread` do caller tính = isItemUnread(item, seenSet).
 */
export function getStatusLabel(item: ItemResponse, t: (k: TranslationKey) => string, unread: boolean): string {
  if (item.type === 'Ticket') return getJiraStatus(item) ?? t(STATUS_KEY[item.status]);
  if (item.status === 'Inbox') return unread ? t('status.unread') : t('status.seen');
  return t(STATUS_KEY[item.status]);
}

/** Item File từ Drive có phải folder không — dùng dropdown parent + icon UI. */
export function isDriveFolder(item: ItemResponse): boolean {
  if (item.type !== 'File' || !item.metadataJson) return false;
  const meta = parseMeta(item);
  if (meta.isFolder === true) return true;
  const mime = meta.mimeType ?? meta.MimeType;
  return typeof mime === 'string' && mime === 'application/vnd.google-apps.folder';
}
