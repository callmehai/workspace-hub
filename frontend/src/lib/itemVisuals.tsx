import { Mail, CalendarDays, FileText, StickyNote, SquareCheckBig, type LucideIcon } from 'lucide-react';
import type { ItemType, ItemStatus } from '../types/items';
import type { TranslationKey } from '../i18n/translations';

/**
 * Danh mục filter dùng chung cho 2 view workspace (Danh sách + Bảng). labelKey → dịch qua t().
 * Đa chọn (như tag): không chọn gì = không lọc; bấm lại chip đang active để tắt.
 */
export const TYPE_FILTERS: { labelKey: TranslationKey; value: ItemType }[] = [
  { labelKey: 'type.email', value: 'Email' },
  { labelKey: 'type.event', value: 'Event' },
  { labelKey: 'type.file', value: 'File' },
  { labelKey: 'type.ticket', value: 'Ticket' },
  { labelKey: 'type.note', value: 'Note' },
];

/**
 * Tab "Nguồn" (integration) ở sidebar trái — mỗi tab = 1 integration, scope trang theo 1 loại.
 * Chọn 1 tab: Inbox chỉ hiện item của loại đó + mở filter riêng (Email → label; Jira → space).
 * "Tất cả mục" (không tab nào) = xem mọi loại, chip loại đa chọn như cũ. Note KHÔNG phải integration.
 */
export const INTEGRATION_TABS: { type: ItemType; labelKey: TranslationKey; Icon: LucideIcon }[] = [
  { type: 'Email', labelKey: 'integration.email', Icon: Mail },
  { type: 'Event', labelKey: 'integration.calendar', Icon: CalendarDays },
  { type: 'File', labelKey: 'integration.drive', Icon: FileText },
  { type: 'Ticket', labelKey: 'integration.jira', Icon: SquareCheckBig },
];

const INTEGRATION_TYPES = new Set<string>(INTEGRATION_TABS.map(t => t.type));

/** Đọc scope integration từ URL (?type=…); trả null nếu không hợp lệ (= tab "Tất cả mục"). */
export const parseSourceType = (v: string | null): ItemType | null =>
  v && INTEGRATION_TYPES.has(v) ? (v as ItemType) : null;

const INTEGRATION_KEY: Record<string, TranslationKey> = {
  Email: 'integration.email', Event: 'integration.calendar', File: 'integration.drive', Ticket: 'integration.jira',
};
export const integrationLabelKey = (type: ItemType): TranslationKey => INTEGRATION_KEY[type] ?? typeLabelKey(type);

export const STATUS_FILTERS: { labelKey: TranslationKey; value: ItemStatus }[] = [
  { labelKey: 'kanban.colInbox', value: 'Inbox' },
  { labelKey: 'kanban.colDoing', value: 'Doing' },
  { labelKey: 'kanban.colDone', value: 'Done' },
];

/** Key i18n cho nhãn loại item — dùng ở chip lọc, chip "đang lọc", nhãn thẻ Kanban, drawer. */
const TYPE_KEY: Record<ItemType, TranslationKey> = {
  Email: 'type.email', Event: 'type.event', File: 'type.file', Note: 'type.note', Ticket: 'type.ticket',
};
export const typeLabelKey = (type: ItemType): TranslationKey => TYPE_KEY[type] ?? 'type.note';

export function typeIcon(t: ItemType, cls = 'w-4 h-4', strokeWidth = 2) {
  switch (t) {
    case 'Email': return <Mail className={cls} strokeWidth={strokeWidth} />;
    case 'Event': return <CalendarDays className={cls} strokeWidth={strokeWidth} />;
    case 'File': return <FileText className={cls} strokeWidth={strokeWidth} />;
    case 'Note': return <StickyNote className={cls} strokeWidth={strokeWidth} />;
    case 'Ticket': return <SquareCheckBig className={cls} strokeWidth={strokeWidth} />;
  }
}

/**
 * Tile "avatar" đặc màu (gradient) + icon trắng — dùng ở list/board cho nổi bật, chuyên nghiệp
 * kiểu Gmail (thay tile pastel mờ). Mỗi loại 1 gam màu để nhận diện nhanh.
 */
const TYPE_SOLID: Record<ItemType, string> = {
  Email: 'bg-gradient-to-br from-blue-500 to-blue-600 text-white',
  Event: 'bg-gradient-to-br from-amber-500 to-orange-500 text-white',
  File: 'bg-gradient-to-br from-emerald-500 to-teal-600 text-white',
  Note: 'bg-gradient-to-br from-slate-400 to-slate-500 text-white dark:from-slate-500 dark:to-slate-600',
  Ticket: 'bg-gradient-to-br from-violet-500 to-purple-600 text-white',
};
export const typeSolidTileClass = (t: ItemType): string =>
  TYPE_SOLID[t] ?? 'bg-gradient-to-br from-slate-400 to-slate-500 text-white';
