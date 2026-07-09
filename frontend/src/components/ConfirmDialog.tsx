import { useEffect } from 'react';
import { createPortal } from 'react-dom';
import { AlertTriangle, Loader2 } from 'lucide-react';
import { useI18n } from '../hooks/useI18n';

interface ConfirmDialogProps {
  open: boolean;
  title?: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  /** 'danger' = nút xác nhận đỏ (xoá); 'primary' = xanh brand. */
  tone?: 'danger' | 'primary';
  loading?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

/**
 * Hộp thoại xác nhận tái dùng — thay `window.confirm` (native, xấu, không theme).
 * Portal + backdrop mờ, đóng bằng Esc / click nền / nút Huỷ.
 */
export function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel,
  cancelLabel,
  tone = 'danger',
  loading = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const { t } = useI18n();

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !loading) onCancel();
      if (e.key === 'Enter' && !loading) onConfirm();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, loading, onCancel, onConfirm]);

  if (!open) return null;

  const confirmClasses =
    tone === 'danger'
      ? 'bg-rose-600 hover:bg-rose-700 focus-visible:ring-rose-400'
      : 'bg-brand-600 hover:bg-brand-700 focus-visible:ring-brand-400';

  return createPortal(
    <div
      className="fixed inset-0 z-[10000] flex items-center justify-center p-4 bg-slate-900/50 backdrop-blur-sm animate-[wh-fade_120ms_ease-out]"
      onClick={() => { if (!loading) onCancel(); }}
    >
      <div
        role="alertdialog"
        aria-modal="true"
        className="w-full max-w-sm rounded-2xl bg-white dark:bg-slate-800 shadow-2xl ring-1 ring-slate-900/5 dark:ring-white/10 overflow-hidden animate-[wh-pop_140ms_cubic-bezier(0.16,1,0.3,1)]"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="p-5 flex gap-4">
          <span
            className={`shrink-0 w-10 h-10 rounded-full flex items-center justify-center ${
              tone === 'danger'
                ? 'bg-rose-100 text-rose-600 dark:bg-rose-500/15 dark:text-rose-400'
                : 'bg-brand-100 text-brand-600 dark:bg-brand-500/15 dark:text-brand-400'
            }`}
          >
            <AlertTriangle className="w-5 h-5" />
          </span>
          <div className="min-w-0 flex-1 pt-0.5">
            <h3 className="text-[15px] font-semibold text-slate-900 dark:text-slate-100">
              {title ?? t('common.confirmTitle')}
            </h3>
            <p className="mt-1 text-[13.5px] leading-relaxed text-slate-500 dark:text-slate-400">
              {message}
            </p>
          </div>
        </div>

        <div className="px-5 py-3.5 flex justify-end gap-2 bg-slate-50 dark:bg-slate-800/60 border-t border-slate-100 dark:border-slate-700/60">
          <button
            type="button"
            onClick={onCancel}
            disabled={loading}
            className="h-9 px-4 rounded-lg text-[13px] font-semibold text-slate-600 dark:text-slate-300 hover:bg-slate-200/70 dark:hover:bg-slate-700 transition-colors disabled:opacity-50"
          >
            {cancelLabel ?? t('common.cancel')}
          </button>
          <button
            type="button"
            onClick={onConfirm}
            disabled={loading}
            className={`h-9 px-4 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold text-white shadow-sm transition-colors focus:outline-none focus-visible:ring-2 disabled:opacity-60 ${confirmClasses}`}
          >
            {loading && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
            {confirmLabel ?? t('common.delete')}
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
