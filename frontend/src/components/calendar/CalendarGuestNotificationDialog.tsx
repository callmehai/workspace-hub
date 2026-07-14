import { useEffect } from 'react';
import { createPortal } from 'react-dom';
import { Loader2, Mail } from 'lucide-react';
import { useI18n } from '../../hooks/useI18n';

interface CalendarGuestNotificationDialogProps {
  open: boolean;
  addedCount: number;
  removedCount: number;
  saving?: boolean;
  onSend: () => void;
  onDontSend: () => void;
  onBack: () => void;
}

export function CalendarGuestNotificationDialog({
  open,
  addedCount,
  removedCount,
  saving = false,
  onSend,
  onDontSend,
  onBack,
}: CalendarGuestNotificationDialogProps) {
  const { lang } = useI18n();

  useEffect(() => {
    if (!open) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !saving) onBack();
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [open, saving, onBack]);

  if (!open) return null;

  const hasAdded = addedCount > 0;
  const hasRemoved = removedCount > 0;
  const message = lang === 'vi'
    ? hasAdded && hasRemoved
      ? `Bạn đã thêm ${addedCount} và xóa ${removedCount} người tham gia. Bạn có muốn Google Calendar gửi email cập nhật cho họ không?`
      : hasRemoved
        ? `Bạn đã xóa ${removedCount} người tham gia. Bạn có muốn Google Calendar gửi email thông báo hủy lời mời cho họ không?`
        : `Bạn đã thêm ${addedCount} người tham gia. Bạn có muốn Google Calendar gửi email mời cho họ không?`
    : hasAdded && hasRemoved
      ? `You added ${addedCount} and removed ${removedCount} guest(s). Do you want Google Calendar to email them about this update?`
      : hasRemoved
        ? `You removed ${removedCount} guest(s). Do you want Google Calendar to email them that the invitation was cancelled?`
        : `You added ${addedCount} guest(s). Do you want Google Calendar to email their invitations?`;

  return createPortal(
    <div
      className="fixed inset-0 z-[11000] flex items-center justify-center bg-slate-950/55 p-4 backdrop-blur-sm animate-[wh-fade_120ms_ease-out]"
      onMouseDown={() => { if (!saving) onBack(); }}
    >
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="calendar-guest-notification-title"
        aria-describedby="calendar-guest-notification-message"
        className="w-full max-w-md overflow-hidden rounded-2xl bg-white shadow-2xl ring-1 ring-slate-900/10 dark:bg-slate-800 dark:ring-white/10 animate-[wh-pop_140ms_cubic-bezier(0.16,1,0.3,1)]"
        onMouseDown={event => event.stopPropagation()}
      >
        <div className="flex gap-4 p-5">
          <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-brand-100 text-brand-600 dark:bg-brand-500/15 dark:text-brand-300">
            <Mail className="h-5 w-5" />
          </span>
          <div className="min-w-0 flex-1 pt-0.5">
            <h3 id="calendar-guest-notification-title" className="text-[15px] font-semibold text-slate-900 dark:text-slate-100">
              {lang === 'vi' ? 'Gửi email cập nhật tới người tham gia?' : 'Email guests about this update?'}
            </h3>
            <p id="calendar-guest-notification-message" className="mt-1.5 text-[13.5px] leading-relaxed text-slate-500 dark:text-slate-400">
              {message}
            </p>
          </div>
        </div>

        <div className="flex flex-wrap justify-end gap-2 border-t border-slate-100 bg-slate-50 px-5 py-3.5 dark:border-slate-700/60 dark:bg-slate-800/60">
          <button
            type="button"
            onClick={onBack}
            disabled={saving}
            className="h-9 rounded-lg px-3.5 text-[13px] font-semibold text-slate-600 transition-colors hover:bg-slate-200/70 disabled:opacity-50 dark:text-slate-300 dark:hover:bg-slate-700"
          >
            {lang === 'vi' ? 'Quay lại chỉnh sửa' : 'Back to editing'}
          </button>
          <button
            type="button"
            onClick={onDontSend}
            disabled={saving}
            className="inline-flex h-9 items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-3.5 text-[13px] font-semibold text-slate-700 shadow-sm transition-colors hover:bg-slate-50 disabled:opacity-50 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-200 dark:hover:bg-slate-700"
          >
            {saving && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            {lang === 'vi' ? 'Không gửi' : "Don't send"}
          </button>
          <button
            type="button"
            onClick={onSend}
            disabled={saving}
            className="inline-flex h-9 items-center gap-1.5 rounded-lg bg-brand-600 px-4 text-[13px] font-semibold text-white shadow-sm transition-colors hover:bg-brand-700 disabled:opacity-60"
          >
            {saving && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            {lang === 'vi' ? 'Gửi' : 'Send'}
          </button>
        </div>
      </div>

      <style>{`
        @keyframes wh-fade { from { opacity: 0; } to { opacity: 1; } }
        @keyframes wh-pop { from { opacity: 0; transform: scale(0.96) translateY(6px); } to { opacity: 1; transform: scale(1) translateY(0); } }
      `}</style>
    </div>,
    document.body,
  );
}
