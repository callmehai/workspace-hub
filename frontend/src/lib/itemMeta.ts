import type { ItemResponse, ItemStatus } from '../types/items';

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

// Nhãn triage chung cho item của mình (Email/Event/File/Note) — 3 status Kanban.
const GENERIC_STATUS: Record<ItemStatus, string> = {
  Inbox: 'Chưa xem', Doing: 'Đang xử lý', Done: 'Hoàn thành',
};

function parseMeta(item: ItemResponse): Record<string, unknown> {
  if (!item.metadataJson) return {};
  try { return JSON.parse(item.metadataJson) as Record<string, unknown>; } catch { return {}; }
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
 * Nhãn trạng thái hiển thị của 1 item, xét theo LOẠI/integration:
 *  - Ticket (Jira): status thô từ Jira (không dịch, không ép về Chưa xem/Đã xem).
 *  - Email (Gmail): Chưa xem | Đã xem (khi đã đọc).
 *  - Còn lại: triage chung.
 */
export function getStatusLabel(item: ItemResponse): string {
  if (item.type === 'Ticket') return getJiraStatus(item) ?? GENERIC_STATUS[item.status] ?? item.status;
  if (item.status === 'Inbox' && item.type === 'Email' && !isEmailUnread(item)) return 'Đã xem';
  return GENERIC_STATUS[item.status] ?? item.status;
}
