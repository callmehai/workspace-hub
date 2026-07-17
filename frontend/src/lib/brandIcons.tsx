import type { FC } from 'react';

/* Logo brand để nhận diện nhanh nguồn item: Gmail / Google Calendar / Google Drive / Jira.
   Tái dùng đúng file SVG chuẩn ở public/icons/ (giống trang Kết nối dịch vụ). Note = icon note (currentColor). */
export type IconProps = { className?: string };

const cls = (c?: string) => `${c ?? ''} object-contain`;

export const GmailIcon: FC<IconProps> = ({ className }) => (
  <img src="/icons/gmail.svg" alt="Gmail" className={cls(className)} aria-hidden draggable={false} />
);
export const CalendarIcon: FC<IconProps> = ({ className }) => (
  <img src="/icons/gcal.svg" alt="Google Calendar" className={cls(className)} aria-hidden draggable={false} />
);
export const DriveIcon: FC<IconProps> = ({ className }) => (
  <img src="/icons/drive.svg" alt="Google Drive" className={cls(className)} aria-hidden draggable={false} />
);
export const JiraIcon: FC<IconProps> = ({ className }) => (
  <img src="/icons/jira.svg" alt="Jira" className={cls(className)} aria-hidden draggable={false} />
);
export const NoteIcon: FC<IconProps> = ({ className }) => (
  <img src="/icons/note.svg" alt="Note" className={cls(className)} aria-hidden draggable={false} />
);
export const FolderIcon: FC<IconProps> = ({ className }) => (
  <img src="/icons/folder.svg" alt="Folder" className={cls(className)} aria-hidden draggable={false} />
);
