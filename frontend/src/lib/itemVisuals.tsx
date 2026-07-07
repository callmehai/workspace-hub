import { Mail, Calendar, FileText, StickyNote, Briefcase } from 'lucide-react';
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
  { labelKey: 'type.note', value: 'Note' },
  { labelKey: 'type.ticket', value: 'Ticket' },
];

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

export function typeIcon(t: ItemType, cls = 'w-4 h-4') {
  switch (t) {
    case 'Email': return <Mail className={cls} />;
    case 'Event': return <Calendar className={cls} />;
    case 'File': return <FileText className={cls} />;
    case 'Note': return <StickyNote className={cls} />;
    case 'Ticket': return <Briefcase className={cls} />;
  }
}
