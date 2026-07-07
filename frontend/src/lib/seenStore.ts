import { useSyncExternalStore } from 'react';

/**
 * Theo dõi trạng thái "đã xem" cho các item KHÔNG phải Email (Event/File/Note) —
 * provider của chúng không có nhãn read/unread, nên app tự lưu client-side.
 *
 * Lưu localStorage (key `wh-seen`) = danh sách itemId đã mở detail trong app.
 * Email vẫn dùng nhãn read/unread THẬT của Gmail (isEmailUnread), không đụng vào đây.
 * Ticket có status Jira riêng → không tính seen.
 *
 * Dùng useSyncExternalStore để list/board/drawer tự re-render khi markSeen chạy.
 */

const KEY = 'wh-seen';

function load(): Set<string> {
  try {
    const raw = localStorage.getItem(KEY);
    const arr = raw ? JSON.parse(raw) : [];
    return Array.isArray(arr) ? new Set(arr as string[]) : new Set();
  } catch {
    return new Set();
  }
}

let seen: Set<string> = load();
const listeners = new Set<() => void>();

function persist() {
  try {
    localStorage.setItem(KEY, JSON.stringify([...seen]));
  } catch {
    /* quota/private mode — bỏ qua, chỉ mất persistence */
  }
}

/** Đánh dấu 1 item đã xem. Tạo Set mới (ref khác) để useSyncExternalStore phát hiện đổi. */
export function markSeen(id: string): void {
  if (seen.has(id)) return;
  seen = new Set(seen);
  seen.add(id);
  persist();
  listeners.forEach((l) => l());
}

/** Đánh dấu lại "chưa xem" (đảo markSeen) — cho nút Mark unread ở non-email item. */
export function markUnseen(id: string): void {
  if (!seen.has(id)) return;
  seen = new Set(seen);
  seen.delete(id);
  persist();
  listeners.forEach((l) => l());
}

function subscribe(cb: () => void): () => void {
  listeners.add(cb);
  return () => { listeners.delete(cb); };
}

function getSnapshot(): Set<string> {
  return seen;
}

/** Hook: trả về Set các itemId đã xem; component re-render khi có markSeen. */
export function useSeenSet(): Set<string> {
  return useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
}
