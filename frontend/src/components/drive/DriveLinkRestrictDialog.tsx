import { createPortal } from 'react-dom';
import { useEffect } from 'react';
import { FileText, Folder, Loader2 } from 'lucide-react';
import { useI18n } from '../../hooks/useI18n';
import type { DriveLinkRestrictConflict } from '../../types/drive';

interface Props {
  open: boolean;
  conflict: DriveLinkRestrictConflict | null;
  loading?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

/** Link "Tìm hiểu thêm" — trang hỗ trợ chia sẻ Drive của Google. */
const LEARN_MORE_URL = 'https://support.google.com/drive/answer/7166529';

/**
 * Popup Case 1 — bố cục giống Google Drive:
 * tiêu đề + mô tả + cây folder→file (anyone → hạn chế) + Huỷ / Xoá khỏi thư mục mẹ.
 */
export function DriveLinkRestrictDialog({
  open,
  conflict,
  loading = false,
  onConfirm,
  onCancel,
}: Props) {
  const { t } = useI18n();

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !loading) onCancel();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, loading, onCancel]);

  if (!open || !conflict) return null;

  const accessLabel = (access: string) =>
    access === 'anyone' ? t('drive.share.accessAnyone') : t('drive.share.accessRestricted');

  return createPortal(
    <div
      className="fixed inset-0 z-[10001] flex items-center justify-center p-4 bg-black/40"
      onClick={() => {
        if (!loading) onCancel();
      }}
    >
      {/* Card trắng bo góc — gần layout dialog Drive */}
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="drive-link-restrict-title"
        className="w-full max-w-[440px] rounded-3xl bg-white dark:bg-slate-900 shadow-2xl overflow-hidden"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="px-6 pt-6 pb-2">
          <h2
            id="drive-link-restrict-title"
            className="text-[22px] font-normal leading-snug text-[#202124] dark:text-slate-100"
          >
            {t('drive.share.restrictParentTitle')}
          </h2>
          <p className="mt-3 text-[14px] leading-relaxed text-[#3c4043] dark:text-slate-300">
            {t('drive.share.restrictParentBody')}{' '}
            <a
              href={LEARN_MORE_URL}
              target="_blank"
              rel="noopener noreferrer"
              className="text-[#1a73e8] hover:underline font-medium"
            >
              {t('drive.share.restrictParentLearnMore')}
            </a>
          </p>

          {/* Cây quyền: folder mẹ → file (giống screenshot Drive) */}
          <div className="mt-5 space-y-0">
            <div className="flex gap-3 items-start">
              <span className="mt-0.5 w-9 h-9 rounded-full bg-[#e8f0fe] dark:bg-blue-500/15 flex items-center justify-center shrink-0">
                <Folder className="w-5 h-5 text-[#1a73e8]" fill="currentColor" fillOpacity={0.15} />
              </span>
              <div className="min-w-0 pt-0.5">
                <div className="text-[14px] font-medium text-[#202124] dark:text-slate-100 truncate">
                  {conflict.parentTitle}
                </div>
                <div className="text-[12px] text-[#5f6368] dark:text-slate-400 mt-0.5">
                  {accessLabel(conflict.parentFromAccess)}
                  <span className="mx-1">→</span>
                  {accessLabel(conflict.parentToAccess)}
                </div>
              </div>
            </div>

            {/* Đường nối cây */}
            <div className="flex gap-3 ml-[17px] border-l-2 border-[#dadce0] dark:border-slate-600 pl-5 py-1">
              <div className="flex gap-3 items-start -ml-[2px] w-full">
                <span className="mt-0.5 w-9 h-9 rounded-full bg-[#e8f0fe] dark:bg-blue-500/15 flex items-center justify-center shrink-0">
                  <FileText className="w-5 h-5 text-[#1a73e8]" />
                </span>
                <div className="min-w-0 pt-0.5">
                  <div className="text-[14px] font-medium text-[#202124] dark:text-slate-100 truncate">
                    {conflict.itemTitle}
                  </div>
                  <div className="text-[12px] text-[#5f6368] dark:text-slate-400 mt-0.5">
                    {accessLabel(conflict.itemFromAccess)}
                    <span className="mx-1">→</span>
                    {accessLabel(conflict.itemToAccess)}
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <div className="px-4 py-4 flex justify-end gap-2">
          <button
            type="button"
            disabled={loading}
            onClick={onCancel}
            className="h-10 px-4 rounded-full text-[14px] font-medium text-[#1a73e8] hover:bg-[#f6fafe] dark:hover:bg-slate-800 disabled:opacity-50"
          >
            {t('drive.share.restrictParentCancel')}
          </button>
          <button
            type="button"
            disabled={loading}
            onClick={onConfirm}
            className="h-10 px-6 rounded-full text-[14px] font-medium text-white bg-[#1a73e8] hover:bg-[#1765cc] disabled:opacity-50 inline-flex items-center gap-2"
          >
            {loading && <Loader2 className="w-4 h-4 animate-spin" />}
            {t('drive.share.restrictParentConfirm')}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
