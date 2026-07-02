import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { formatDistanceToNow } from 'date-fns';
import { vi } from 'date-fns/locale';
import { useNavigate } from 'react-router-dom';
import { notificationsApi } from '../../lib/notificationsApi';
import { handleApiError } from '../../lib/errorUtils';
import { UNREAD_COUNT_KEY } from '../../hooks/useNotificationHub';
import type { NotificationDto } from '../../types/notifications';

interface NotificationsDropdownProps {
  onClose: () => void;
}

export const NotificationsDropdown = ({ onClose }: NotificationsDropdownProps) => {
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  const { data, isLoading } = useQuery({
    queryKey: ['notifications', { skip: 0, top: 20 }],
    queryFn: () => notificationsApi.getNotifications(0, 20),
  });

  const markAsRead = useMutation({
    mutationFn: (id: string) => notificationsApi.markAsRead(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['notifications'] });
      queryClient.invalidateQueries({ queryKey: UNREAD_COUNT_KEY });
    },
    onError: (err) => handleApiError(err, 'Không thể đánh dấu đã đọc'),
  });

  const markAllAsRead = useMutation({
    mutationFn: () => notificationsApi.markAllAsRead(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['notifications'] });
      queryClient.setQueryData(UNREAD_COUNT_KEY, 0);
    },
    onError: (err) => handleApiError(err, 'Không thể đánh dấu tất cả đã đọc'),
  });

  const items = data?.value ?? [];

  const handleItemClick = (notification: NotificationDto) => {
    if (!notification.isRead) {
      markAsRead.mutate(notification.id);
    }
    if (notification.linkUrl) {
      navigate(notification.linkUrl);
      onClose();
    }
  };

  return (
    <div className="absolute right-0 top-full mt-2 w-80 bg-white border border-gray-200 shadow-lg rounded-lg z-50 overflow-hidden">
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100">
        <h3 className="text-sm font-semibold text-gray-900">Thông báo</h3>
        {items.some((n) => !n.isRead) && (
          <button
            type="button"
            id="mark-all-read-btn"
            onClick={() => markAllAsRead.mutate()}
            disabled={markAllAsRead.isPending}
            className="text-xs text-brand-600 hover:text-brand-700 font-medium disabled:opacity-50"
          >
            Đánh dấu tất cả đã đọc
          </button>
        )}
      </div>

      <div className="max-h-96 overflow-y-auto">
        {isLoading && (
          <div className="px-4 py-6 space-y-3">
            {[1, 2, 3].map((i) => (
              <div key={i} className="animate-pulse space-y-2">
                <div className="h-3 bg-gray-100 rounded w-3/4" />
                <div className="h-2 bg-gray-100 rounded w-full" />
              </div>
            ))}
          </div>
        )}

        {!isLoading && items.length === 0 && (
          <p className="px-4 py-8 text-sm text-gray-500 text-center">Không có thông báo</p>
        )}

        {!isLoading &&
          items.map((notification) => (
            <button
              key={notification.id}
              id={`notification-${notification.id}`}
              type="button"
              onClick={() => handleItemClick(notification)}
              className={`w-full text-left px-4 py-3 border-b border-gray-50 hover:bg-gray-50 transition-colors ${!notification.isRead ? 'bg-brand-50/40' : ''
                }`}
            >
              <div className="flex items-start gap-2">
                {!notification.isRead && (
                  <span className="mt-1.5 w-2 h-2 rounded-full bg-brand-500 shrink-0" />
                )}
                <div className={notification.isRead ? 'pl-4' : ''}>
                  <p className="text-sm font-medium text-gray-900 line-clamp-1">{notification.title}</p>
                  <p className="text-xs text-gray-500 mt-0.5 line-clamp-2">{notification.body}</p>
                  <p className="text-xs text-gray-400 mt-1">
                    {formatDistanceToNow(new Date(notification.createdAt), {
                      addSuffix: true,
                      locale: vi,
                    })}
                  </p>
                </div>
              </div>
            </button>
          ))}
      </div>
    </div>
  );
};
