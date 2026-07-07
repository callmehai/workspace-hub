import { createContext } from 'react';

export type Theme = 'light' | 'dark';

export interface ThemeContextValue {
  theme: Theme;
  /** Đặt theme cụ thể (Sáng/Tối). */
  setTheme: (t: Theme) => void;
  /** Đảo Sáng ↔ Tối. */
  toggleTheme: () => void;
}

export const ThemeContext = createContext<ThemeContextValue | undefined>(undefined);

export const THEME_STORAGE_KEY = 'wh-theme';
