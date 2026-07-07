import { Search, Bell, Plus } from 'lucide-react';
import { useAuth } from '../../hooks/useAuth';
import { useI18n } from '../../hooks/useI18n';
import { Link } from 'react-router-dom';
import { ThemeLangControls } from '../ThemeLangControls';

export const Header = () => {
  const { user } = useAuth();
  const { t } = useI18n();

  return (
    <header className="h-16 border-b border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900 flex items-center justify-between gap-4 px-6 shrink-0">
      <div className="flex-1 max-w-2xl">
        <div className="relative group">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400 group-focus-within:text-brand-500" />
          <input
            type="text"
            placeholder={t('header.search')}
            className="w-full bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-md py-1.5 pl-10 pr-4 text-sm text-slate-800 dark:text-slate-100 focus:outline-none focus:border-brand-500 focus:ring-1 focus:ring-brand-500 placeholder-slate-400 dark:placeholder-slate-500 transition-colors"
          />
        </div>
      </div>

      <div className="flex items-center gap-2 shrink-0">
        <Link
          to="/integrations"
          className="inline-flex items-center gap-1.5 h-8 px-3.5 rounded-md bg-brand-600 text-white text-sm font-medium hover:bg-brand-700 transition-colors shadow-sm"
        >
          <Plus className="w-4 h-4" />
          <span className="hidden sm:inline">{t('header.connect')}</span>
        </Link>

        <ThemeLangControls />

        <button
          aria-label={t('header.notifications')}
          title={t('header.notifications')}
          className="relative flex h-8 w-8 items-center justify-center rounded-md text-slate-500 hover:bg-slate-100 hover:text-slate-700 dark:text-slate-400 dark:hover:bg-slate-800 dark:hover:text-slate-200 transition-colors"
        >
          <Bell className="w-5 h-5" />
          <span className="absolute top-1 right-1.5 w-2 h-2 bg-red-500 rounded-full border-2 border-white dark:border-slate-900" />
        </button>

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
