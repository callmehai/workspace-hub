import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { ThemeContext, THEME_STORAGE_KEY, type Theme } from './theme-context';

/**
 * Đọc theme khởi tạo: localStorage → prefers-color-scheme → 'light'.
 * (index.html có inline script set sẵn class `.dark` trước paint để tránh nháy sáng→tối;
 *  ở đây chỉ đọc lại state cho React đồng bộ với DOM.)
 */
function getInitialTheme(): Theme {
  if (typeof window === 'undefined') return 'light';
  const saved = localStorage.getItem(THEME_STORAGE_KEY);
  if (saved === 'light' || saved === 'dark') return saved;
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

/**
 * ThemeProvider — đổi theme = TOGGLE class `.dark` trên <html> + lưu localStorage.
 * Đây là thao tác DOM thuần, KHÔNG remount cây React → mọi component (kể cả list đang
 * scroll, form đang nhập, query cache) giữ nguyên state khi đổi Sáng/Tối.
 */
export const ThemeProvider = ({ children }: { children: ReactNode }) => {
  const [theme, setThemeState] = useState<Theme>(getInitialTheme);

  useEffect(() => {
    const root = document.documentElement;
    root.classList.toggle('dark', theme === 'dark');
    root.style.colorScheme = theme; // để form control gốc (scrollbar, input date…) theo theme
    localStorage.setItem(THEME_STORAGE_KEY, theme);
  }, [theme]);

  const setTheme = useCallback((t: Theme) => setThemeState(t), []);
  const toggleTheme = useCallback(
    () => setThemeState((t) => (t === 'dark' ? 'light' : 'dark')),
    [],
  );

  return (
    <ThemeContext.Provider value={{ theme, setTheme, toggleTheme }}>
      {children}
    </ThemeContext.Provider>
  );
};
