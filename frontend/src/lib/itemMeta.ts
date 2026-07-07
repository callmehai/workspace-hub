import type { ItemResponse } from '../types/items';

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
