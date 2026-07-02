import { useEffect, useRef, useState } from 'react';
import { Search, Bell, Plus } from 'lucide-react';
import { useQuery } from '@tanstack/react-query';
import { useAuth } from '../../hooks/useAuth';
import { Link } from 'react-router-dom';
import { notificationsApi } from '../../lib/notificationsApi';
import { UNREAD_COUNT_KEY } from '../../hooks/useNotificationHub';
import { NotificationsDropdown } from './NotificationsDropdown';

export const Header = () => {
  const { user } = useAuth();
  const [isOpen, setIsOpen] = useState(false);
  const bellRef = useRef<HTMLDivElement>(null);

  const { data: unreadCount = 0 } = useQuery({
    queryKey: UNREAD_COUNT_KEY,
    queryFn: () => notificationsApi.getUnreadCount(),
    staleTime: 30_000,
    refetchOnWindowFocus: true,
  });

  useEffect(() => {
    if (!isOpen) return;

    const handleClickOutside = (e: MouseEvent) => {
      if (bellRef.current && !bellRef.current.contains(e.target as Node)) {
        setIsOpen(false);
      }
    };

    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [isOpen]);

  return (
    <header className="h-16 border-b border-gray-200 bg-gray-50 flex items-center justify-between px-6 shrink-0">
      <div className="flex-1 max-w-2xl">
        <div className="relative group">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500 group-focus-within:text-brand-500" />
          <input
            type="text"
            placeholder="Search across tools..."
            className="w-full bg-white border border-gray-200 rounded-md py-1.5 pl-10 pr-4 text-sm text-gray-800 focus:outline-none focus:border-brand-500 focus:ring-1 focus:ring-brand-500 placeholder-gray-500 transition-colors"
          />
        </div>
      </div>

      <div className="flex items-center space-x-4 ml-4">
        <Link
          to="/integrations"
          className="inline-flex items-center gap-1.5 h-8 px-3.5 border border-transparent rounded-md bg-brand-600 text-white text-sm font-medium hover:bg-brand-700 transition-colors shadow-sm"
        >
          <Plus className="w-4 h-4" />
          <span>Kết nối dịch vụ</span>
        </Link>

        <div className="relative" ref={bellRef}>
          <button
            type="button"
            onClick={() => setIsOpen((prev) => !prev)}
            className="text-gray-500 hover:text-gray-700 transition-colors relative p-1"
            aria-label="Thông báo"
          >
            <Bell className="w-5 h-5" />
            {unreadCount > 0 && (
              <span className="absolute -top-0.5 -right-0.5 min-w-[18px] h-[18px] px-1 flex items-center justify-center text-[10px] font-semibold text-white bg-red-500 rounded-full border-2 border-gray-50">
                {unreadCount > 99 ? '99+' : unreadCount}
              </span>
            )}
          </button>
          {isOpen && <NotificationsDropdown onClose={() => setIsOpen(false)} />}
        </div>

        <div className="h-8 w-8 rounded-full bg-gradient-to-tr from-brand-500 to-purple-500 flex items-center justify-center text-sm font-medium text-white shadow-sm overflow-hidden border border-gray-200">
          {user?.fullName ? user.fullName.charAt(0).toUpperCase() : 'U'}
        </div>
      </div>
    </header>
  );
};
