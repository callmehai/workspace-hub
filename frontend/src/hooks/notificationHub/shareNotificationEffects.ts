import type { QueryClient } from '@tanstack/react-query';
import type { NotificationDto } from '../../types/notifications';

export interface ShareNotificationPayload {
  from?: string;
  folder?: string;
  shareId?: string;
  folderId?: string;
  permission?: string;
}

export function parseShareNotificationPayload(
  body: string,
): ShareNotificationPayload | null {
  try {
    const parsed: unknown = JSON.parse(body);
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return null;
    return parsed as ShareNotificationPayload;
  } catch {
    return null;
  }
}

/** Invalidate React Query khi nhận ShareInvite / ShareAccepted / ShareDeclined qua SignalR hub chung. */

const OWNER_SHARE_UPDATE_TYPES: ReadonlySet<NotificationDto['type']> = new Set([
  'ShareAccepted',
  'ShareDeclined',
]);

export function applyFolderShareNotificationEffects(
  queryClient: QueryClient,
  notification: NotificationDto,
): void {
  if (
    notification.type !== 'ShareInvite'
    && !OWNER_SHARE_UPDATE_TYPES.has(notification.type)
  ) {
    return;
  }

  const payload = parseShareNotificationPayload(notification.body);

  // Invitee: sidebar pending + sau accept có folder trong danh sách
  if (notification.type === 'ShareInvite') {
    void queryClient.invalidateQueries({ queryKey: ['sharedWithMe'] });
    return;
  }

  // Owner: modal FolderShareDialog refetch (accept → Accepted, decline → biến mất)
  if (payload?.folderId) {
    void queryClient.invalidateQueries({
      queryKey: ['folderShares', payload.folderId],
    });
  } else {
    void queryClient.invalidateQueries({ queryKey: ['folderShares'] });
  }

  void queryClient.invalidateQueries({ queryKey: ['folders'] });
}
