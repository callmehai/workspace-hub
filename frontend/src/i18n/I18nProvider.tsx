import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { I18nContext, LANG_STORAGE_KEY } from './i18n-context';
import { dictionaries, type Lang, type TranslationKey } from './translations';

function getInitialLang(): Lang {
  if (typeof window === 'undefined') return 'vi';
  const saved = localStorage.getItem(LANG_STORAGE_KEY);
  if (saved === 'vi' || saved === 'en') return saved;
  return 'vi'; // mặc định tiếng Việt (đồ án VN)
}

/**
 * I18nProvider — đổi ngôn ngữ = đổi `lang` trong context state.
 * React CHỈ re-render các component có đọc `useI18n()` (chuỗi mới), KHÔNG unmount/mount lại
 * cây → không mất state input, không refetch query, không nháy layout. Đúng yêu cầu
 * "đổi lang không load lại component nào".
 */
export const I18nProvider = ({ children }: { children: ReactNode }) => {
  const [lang, setLangState] = useState<Lang>(getInitialLang);

  useEffect(() => {
    localStorage.setItem(LANG_STORAGE_KEY, lang);
    document.documentElement.lang = lang; // a11y + đúng ngữ pháp trình duyệt
  }, [lang]);

  const setLang = useCallback((l: Lang) => setLangState(l), []);
  const toggleLang = useCallback(
    () => setLangState((l) => (l === 'vi' ? 'en' : 'vi')),
    [],
  );

  const t = useCallback(
    (key: TranslationKey, vars?: Record<string, string | number>) => {
      let str: string = dictionaries[lang][key] ?? dictionaries.vi[key] ?? key;
      if (vars) {
        for (const [k, v] of Object.entries(vars)) {
          str = str.replaceAll(`{${k}}`, String(v));
        }
      }
      return str;
    },
    [lang],
  );

  const value = useMemo(
    () => ({ lang, setLang, toggleLang, t }),
    [lang, setLang, toggleLang, t],
  );

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
};
