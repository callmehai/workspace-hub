import { formatDistanceToNow } from 'date-fns';
import { vi, enUS } from 'date-fns/locale';

/** Locale date-fns theo ngôn ngữ UI hiện tại (đọc <html lang> do I18nProvider set). */
export function dfLocale() {
  return document.documentElement.lang === 'en' ? enUS : vi;
}

/**
 * "Khoảng x giờ trước" / "about x hours ago" — theo ngôn ngữ UI.
 * Đọc lang từ <html> nên component gọi trong render sẽ tự đổi khi toggle ngôn ngữ.
 */
export function timeAgo(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return '';
  return formatDistanceToNow(d, { addSuffix: true, locale: dfLocale() });
}
