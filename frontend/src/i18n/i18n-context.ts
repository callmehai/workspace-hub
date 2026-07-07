import { createContext } from 'react';
import type { Lang, TranslationKey } from './translations';

export interface I18nContextValue {
  lang: Lang;
  setLang: (l: Lang) => void;
  /** Đảo VI ↔ EN. */
  toggleLang: () => void;
  /** Dịch key → chuỗi theo `lang` hiện tại. Nội suy biến qua `{name}`. */
  t: (key: TranslationKey, vars?: Record<string, string | number>) => string;
}

export const I18nContext = createContext<I18nContextValue | undefined>(undefined);

export const LANG_STORAGE_KEY = 'wh-lang';
