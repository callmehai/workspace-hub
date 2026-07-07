import { Mail, Calendar, FileText, StickyNote, Briefcase } from 'lucide-react';
import type { ItemType, ItemStatus } from '../types/items';

/** Danh mục filter dùng chung cho 2 view workspace (Danh sách + Bảng). */
export const TYPE_FILTERS: { label: string; value: ItemType | null }[] = [
  { label: 'Mọi loại', value: null },
  { label: 'Email', value: 'Email' },
  { label: 'Sự kiện', value: 'Event' },
  { label: 'Tệp', value: 'File' },
  { label: 'Ghi chú', value: 'Note' },
  { label: 'Ticket', value: 'Ticket' },
];

export const STATUS_FILTERS: { label: string; value: ItemStatus | null }[] = [
  { label: 'Mọi trạng thái', value: null },
  { label: 'Cần xem', value: 'Inbox' },
  { label: 'Đang xử lý', value: 'Doing' },
  { label: 'Hoàn thành', value: 'Done' },
];

export function typeIcon(t: ItemType, cls = 'w-4 h-4') {
  switch (t) {
    case 'Email': return <Mail className={cls} />;
    case 'Event': return <Calendar className={cls} />;
    case 'File': return <FileText className={cls} />;
    case 'Note': return <StickyNote className={cls} />;
    case 'Ticket': return <Briefcase className={cls} />;
  }
}
