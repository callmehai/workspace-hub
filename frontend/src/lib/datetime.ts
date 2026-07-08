import { formatDistanceToNow } from 'date-fns';
import { vi, enUS } from 'date-fns/locale';
import type { Lang } from '../i18n/translations';

/** Locale date-fns theo ngôn ngữ UI truyền vào (lấy từ useI18n().lang). */
export function dfLocale(lang: Lang) {
  return lang === 'en' ? enUS : vi;
}

/**
 * "Khoảng x giờ trước" / "about x hours ago" — theo ngôn ngữ UI.
 * Nhận `lang` từ context (KHÔNG đọc <html lang> — thuộc tính đó do useEffect set
 * sau render nên trễ 1 nhịp, khiến chuỗi vẫn hiển thị ngôn ngữ cũ tới khi reload).
 */
export function timeAgo(iso: string | null | undefined, lang: Lang): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return '';
  return formatDistanceToNow(d, { addSuffix: true, locale: dfLocale(lang) });
}
