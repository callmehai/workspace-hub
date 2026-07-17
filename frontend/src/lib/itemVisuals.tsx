import type { FC } from 'react';
import type { ItemType, ItemStatus } from '../types/items';
import type { TranslationKey } from '../i18n/translations';
import { GmailIcon, CalendarIcon, DriveIcon, JiraIcon, NoteIcon, FolderIcon, type IconProps } from './brandIcons';

/* Icon loại item = logo brand màu (Gmail/Calendar/Drive/Jira), Note = icon note. Xem brandIcons.tsx. */

/** Component logo theo loại (dùng cho tile list/board + tab nguồn + chip lọc). */
const TYPE_ICON: Record<ItemType, FC<IconProps>> = {
  Email: GmailIcon,
  Event: CalendarIcon,
  File: DriveIcon,
  Ticket: JiraIcon,
  Note: NoteIcon,
};

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
 * Icon dùng logo brand thật (Gmail/Calendar/Drive/Jira) cho dễ nhận diện.
 */
export const INTEGRATION_TABS: { type: ItemType; labelKey: TranslationKey; Icon: FC<IconProps> }[] = [
  { type: 'Email', labelKey: 'integration.email', Icon: GmailIcon },
  { type: 'Event', labelKey: 'integration.calendar', Icon: CalendarIcon },
  { type: 'File', labelKey: 'integration.drive', Icon: DriveIcon },
  { type: 'Ticket', labelKey: 'integration.jira', Icon: JiraIcon },
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

/** Icon loại item = logo brand màu (Note = icon note; File+folder = folder xanh). `strokeWidth` giữ cho tương thích, không dùng. */
export function typeIcon(t: ItemType, cls = 'w-4 h-4', _strokeWidth?: number, isFolder = false) {
  if (t === 'File' && isFolder) {
    return <FolderIcon className={cls} />;
  }
  const Icon = TYPE_ICON[t] ?? NoteIcon;
  return <Icon className={cls} />;
}

/** Tile "avatar" cho list/board — mọi logo brand (kể cả Note & folder) đặt trên nền TRẮNG viền nhạt để giữ màu thật. */
const TILE_CLASS = 'bg-white border border-slate-200 dark:border-slate-300';
export const typeSolidTileClass = (_t?: ItemType, _isFolder = false): string => TILE_CLASS;
