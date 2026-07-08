import { useMutation, useInfiniteQuery, useQueryClient } from '@tanstack/react-query';
import { formatDistanceToNow } from 'date-fns';
import { vi } from 'date-fns/locale';
import { Bell, Loader2 } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { notificationsApi } from '../../lib/notificationsApi';
import { handleApiError } from '../../lib/errorUtils';
import { NOTIFICATIONS_LIST_KEY, UNREAD_COUNT_KEY } from '../../hooks/useNotificationHub';
import type { NotificationDto } from '../../types/notifications';

const PAGE_SIZE = 20;

interface NotificationsDropdownProps {
  onClose: () => void;
}

export const NotificationsDropdown = ({ onClose }: NotificationsDropdownProps) => {
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  const { data, isLoading, fetchNextPage, hasNextPage, isFetchingNextPage } = useInfiniteQuery({
    queryKey: NOTIFICATIONS_LIST_KEY,
    queryFn: ({ pageParam }) => notificationsApi.getNotifications(pageParam, PAGE_SIZE),
    initialPageParam: 0,
    getNextPageParam: (lastPage, allPages) => {
      const total = lastPage['@odata.count'] ?? lastPage.value.length;
      const loaded = allPages.reduce((sum, p) => sum + p.value.length, 0);
      return loaded < total ? loaded : undefined;
    },
  });

  const markAsRead = useMutation({
    mutationFn: (id: string) => notificationsApi.markAsRead(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_LIST_KEY });
      queryClient.invalidateQueries({ queryKey: UNREAD_COUNT_KEY });
    },
    onError: (err) => handleApiError(err, 'Không thể đánh dấu đã đọc'),
  });

  const markAllAsRead = useMutation({
    mutationFn: () => notificationsApi.markAllAsRead(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_LIST_KEY });
      queryClient.setQueryData(UNREAD_COUNT_KEY, 0);
    },
    onError: (err) => handleApiError(err, 'Không thể đánh dấu tất cả đã đọc'),
  });

  const items = data?.pages.flatMap((p) => p.value) ?? [];
  const totalCount = data?.pages[0]?.['@odata.count'] ?? items.length;

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
    <div className="absolute right-0 top-full z-50 mt-2 w-80 overflow-hidden rounded-lg border border-slate-200 bg-white shadow-xl ring-1 ring-slate-900/5 dark:border-slate-700 dark:bg-slate-900 dark:ring-white/10">
      <div className="flex items-center justify-between border-b border-slate-100 px-4 py-3 dark:border-slate-800">
        <div className="flex items-center gap-2">
          <Bell className="h-4 w-4 text-brand-600 dark:text-brand-400" aria-hidden />
          <h3 className="text-sm font-semibold text-slate-900 dark:text-slate-100">Thông báo</h3>
          {totalCount > 0 && (
            <span className="text-xs text-slate-400 dark:text-slate-500">({totalCount})</span>
          )}
        </div>
        {items.some((n) => !n.isRead) && (
          <button
            type="button"
            id="mark-all-read-btn"
            onClick={() => markAllAsRead.mutate()}
            disabled={markAllAsRead.isPending}
            className="text-xs font-medium text-brand-600 hover:text-brand-700 disabled:opacity-50 dark:text-brand-400 dark:hover:text-brand-300"
          >
            Đánh dấu tất cả đã đọc
          </button>
        )}
      </div>

      <div className="max-h-96 overflow-y-auto">
        {isLoading && (
          <div className="space-y-3 px-4 py-6">
            {[1, 2, 3].map((i) => (
              <div key={i} className="animate-pulse space-y-2">
                <div className="h-3 w-3/4 rounded bg-slate-100 dark:bg-slate-800" />
                <div className="h-2 w-full rounded bg-slate-100 dark:bg-slate-800" />
              </div>
            ))}
          </div>
        )}

        {!isLoading && items.length === 0 && (
          <p className="px-4 py-8 text-center text-sm text-slate-500 dark:text-slate-400">
            Không có thông báo
          </p>
        )}

        {!isLoading &&
          items.map((notification) => (
            <button
              key={notification.id}
              id={`notification-${notification.id}`}
              type="button"
              onClick={() => handleItemClick(notification)}
              className={`w-full border-b border-slate-50 px-4 py-3 text-left transition-colors last:border-b-0 hover:bg-slate-50 dark:border-slate-800 dark:hover:bg-slate-800/80 ${
                !notification.isRead ? 'bg-brand-50/50 dark:bg-brand-500/10' : ''
              }`}
            >
              <div className="flex items-start gap-2">
                {!notification.isRead && (
                  <span className="mt-1.5 h-2 w-2 shrink-0 rounded-full bg-brand-500 dark:bg-brand-400" />
                )}
                <div className={notification.isRead ? 'pl-4' : ''}>
                  <p className="line-clamp-1 text-sm font-medium text-slate-900 dark:text-slate-100">
                    {notification.title}
                  </p>
                  <p className="mt-0.5 line-clamp-2 text-xs text-slate-500 dark:text-slate-400">
                    {notification.body}
                  </p>
                  <p className="mt-1 text-xs text-slate-400 dark:text-slate-500">
                    {formatDistanceToNow(new Date(notification.createdAt), {
                      addSuffix: true,
                      locale: vi,
                    })}
                  </p>
                </div>
              </div>
            </button>
          ))}

        {hasNextPage && (
          <div className="border-t border-slate-100 px-4 py-2 dark:border-slate-800">
            <button
              type="button"
              onClick={() => fetchNextPage()}
              disabled={isFetchingNextPage}
              className="flex w-full items-center justify-center gap-2 rounded-md py-2 text-xs font-medium text-brand-600 hover:bg-slate-50 disabled:opacity-50 dark:text-brand-400 dark:hover:bg-slate-800/80"
            >
              {isFetchingNextPage ? (
                <>
                  <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden />
                  Đang tải...
                </>
              ) : (
                `Tải thêm (${items.length}/${totalCount})`
              )}
            </button>
          </div>
        )}
      </div>
    </div>
  );
};
