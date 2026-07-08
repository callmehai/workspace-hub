import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Search, Bell, Plus, Menu } from 'lucide-react';
import { useAuth } from '../../hooks/useAuth';
import { useI18n } from '../../hooks/useI18n';
import { Link } from 'react-router-dom';
import { notificationsApi } from '../../lib/notificationsApi';
import { UNREAD_COUNT_KEY } from '../../hooks/useNotificationHub';
import { NotificationsDropdown } from './NotificationsDropdown';
import { ThemeLangControls } from '../ThemeLangControls';

interface HeaderProps {
  onMenuClick?: () => void;
}

export const Header = ({ onMenuClick }: HeaderProps) => {
  const { user } = useAuth();
  const { t } = useI18n();
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
    <header className="h-16 border-b border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900 flex items-center justify-between gap-3 px-4 md:px-6 shrink-0">
      <div className="flex items-center gap-2 min-w-0 flex-1 max-w-2xl">
        <button
          type="button"
          onClick={onMenuClick}
          aria-label={t('nav.openMenu')}
          className="lg:hidden flex h-9 w-9 shrink-0 items-center justify-center rounded-md text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800 transition-colors"
        >
          <Menu className="w-5 h-5" />
        </button>

        <div className="relative group flex-1 min-w-0 hidden md:block">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400 group-focus-within:text-brand-500" />
          <input
            type="text"
            placeholder={t('header.search')}
            className="w-full bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-md py-1.5 pl-10 pr-4 text-sm text-slate-800 dark:text-slate-100 focus:outline-none focus:border-brand-500 focus:ring-1 focus:ring-brand-500 placeholder-slate-400 dark:placeholder-slate-500 transition-colors"
          />
        </div>
      </div>

      <div className="flex items-center gap-1.5 sm:gap-2 shrink-0">
        <Link
          to="/integrations"
          className="inline-flex items-center gap-1.5 h-8 px-2.5 sm:px-3.5 rounded-md bg-brand-600 text-white text-sm font-medium hover:bg-brand-700 transition-colors shadow-sm"
        >
          <Plus className="w-4 h-4" />
          <span className="hidden sm:inline">{t('header.connect')}</span>
        </Link>

        <ThemeLangControls />

        <div className="relative" ref={bellRef}>
          <button
            type="button"
            onClick={() => setIsOpen((prev) => !prev)}
            aria-label={t('header.notifications')}
            title={t('header.notifications')}
            className="relative flex h-8 w-8 items-center justify-center rounded-md text-slate-500 hover:bg-slate-100 hover:text-slate-700 dark:text-slate-400 dark:hover:bg-slate-800 dark:hover:text-slate-200 transition-colors"
          >
            <Bell className="w-5 h-5" />
            {unreadCount > 0 && (
              <span className="absolute -top-0.5 -right-0.5 min-w-[18px] h-[18px] px-1 flex items-center justify-center text-[10px] font-semibold text-white bg-red-500 rounded-full border-2 border-white dark:border-slate-900">
                {unreadCount > 99 ? '99+' : unreadCount}
              </span>
            )}
          </button>
          {isOpen && <NotificationsDropdown onClose={() => setIsOpen(false)} />}
        </div>

        <Link
          to="/profile"
          aria-label={t('nav.profile')}
          title={t('nav.profile')}
          className="h-8 w-8 rounded-full bg-brand-50 dark:bg-slate-800 flex items-center justify-center text-sm font-semibold text-brand-600 dark:text-brand-300 border border-brand-100 dark:border-slate-700 hover:ring-2 hover:ring-brand-200 dark:hover:ring-slate-600 transition-shadow"
        >
          {user?.fullName ? user.fullName.charAt(0).toUpperCase() : 'U'}
        </Link>
      </div>
    </header>
  );
};