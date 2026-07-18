export type NotificationType =
  | 'ShareInvite'
  | 'ImportantEmail'
  | 'SyncError'
  | 'ScheduleSent'
  | 'ItemSynced'
  | 'CalendarReminder'
  | 'CalendarInvite';

export interface NotificationDto {
  id: string;
  type: NotificationType;
  title: string;
  body: string;
  linkUrl: string;
  isRead: boolean;
  createdAt: string;
}
