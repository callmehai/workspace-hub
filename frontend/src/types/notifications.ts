export type NotificationType =
  | 'ShareInvite'
  | 'ShareAccepted'
  | 'ShareDeclined'
  | 'ImportantEmail'
  | 'SyncError'
  | 'ScheduleSent'
  | 'ItemSynced'
  | 'CalendarReminder'
  | 'CalendarInvite'
  | 'FriendRequest'
  | 'FriendAccepted';

export interface NotificationDto {
  id: string;
  type: NotificationType;
  title: string;
  body: string;
  linkUrl: string;
  isRead: boolean;
  createdAt: string;
}
