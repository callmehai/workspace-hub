import { formatDistanceToNow } from 'date-fns';
import { vi, enUS } from 'date-fns/locale';
import type { Lang } from '../i18n/translations';

/** Locale date-fns theo ngôn ngữ UI truyền vào (lấy từ useI18n().lang). */
export function dfLocale(lang: Lang) {
  return lang === 'en' ? enUS : vi;
}

export function localeTag(lang: Lang): string {
  return lang === 'en' ? 'en-US' : 'vi-VN';
}

/**
 * Lang hiện tại ngoài React (toast/util) — cùng nguồn với `translate()`.
 */
export function resolveUiLang(): Lang {
  if (typeof document !== 'undefined') {
    const d = document.documentElement.lang;
    if (d === 'en' || d === 'vi') return d;
  }
  if (typeof localStorage !== 'undefined') {
    const s = localStorage.getItem('wh-lang');
    if (s === 'en' || s === 'vi') return s;
  }
  return 'vi';
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

/** True nếu chuỗi giống ISO datetime (preview legacy của calendar reminder). */
export function looksLikeIsoDateTime(value: string): boolean {
  return /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/.test(value);
}

/**
 * Format thời điểm sự kiện cho noti/invitee UI.
 * All-day (hoặc legacy midnight UTC): chỉ ngày — tránh lệch +7h.
 */
export function formatEventWhen(
  iso: string,
  options?: { allDay?: boolean; lang?: Lang; allDayLabel?: string },
): string {
  const d = new Date(iso);
  if (isNaN(d.getTime())) return iso;

  const lang = options?.lang ?? resolveUiLang();
  const locale = localeTag(lang);
  const utcMidnight =
    d.getUTCHours() === 0 && d.getUTCMinutes() === 0 && d.getUTCSeconds() === 0;
  const allDay = options?.allDay === true || (options?.allDay === undefined && utcMidnight);

  if (allDay) {
    const date = d.toLocaleDateString(locale, {
      weekday: 'long',
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      timeZone: 'UTC',
    });
    return options?.allDayLabel ? `${date} (${options.allDayLabel})` : date;
  }

  return d.toLocaleString(locale, {
    weekday: 'long',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}
