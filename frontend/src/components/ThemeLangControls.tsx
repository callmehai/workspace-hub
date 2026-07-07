import { Moon, Sun } from 'lucide-react';
import { useTheme } from '../hooks/useTheme';
import { useI18n } from '../hooks/useI18n';

/**
 * Cụm điều khiển Theme (Sáng/Tối) + Ngôn ngữ (VI/EN) tái dùng ở Header và các trang auth.
 * Bấm chỉ đổi context/DOM class → KHÔNG remount gì.
 */
export const ThemeLangControls = () => {
  const { theme, toggleTheme } = useTheme();
  const { lang, toggleLang, t } = useI18n();
  const isDark = theme === 'dark';

  return (
    <div className="flex items-center gap-1">
      <button
        type="button"
        onClick={toggleTheme}
        aria-label={isDark ? t('theme.toLight') : t('theme.toDark')}
        title={isDark ? t('theme.toLight') : t('theme.toDark')}
        className="flex h-8 w-8 items-center justify-center rounded-md text-slate-500 hover:bg-slate-100 hover:text-slate-900 dark:text-slate-400 dark:hover:bg-slate-800 dark:hover:text-slate-100 transition-colors"
      >
        {isDark ? <Sun className="h-[18px] w-[18px]" /> : <Moon className="h-[18px] w-[18px]" />}
      </button>

      <button
        type="button"
        onClick={toggleLang}
        aria-label={t('lang.label')}
        title={t('lang.label')}
        className="flex h-8 min-w-8 items-center justify-center rounded-md px-2 text-[12px] font-semibold text-slate-500 hover:bg-slate-100 hover:text-slate-900 dark:text-slate-400 dark:hover:bg-slate-800 dark:hover:text-slate-100 transition-colors"
      >
        {lang === 'vi' ? 'EN' : 'VI'}
      </button>
    </div>
  );
};
