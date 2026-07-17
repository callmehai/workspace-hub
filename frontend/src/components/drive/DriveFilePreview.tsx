import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { ExternalLink, Loader2, ZoomIn, X } from 'lucide-react';
import { driveApi } from '../../lib/driveApi';
import { drivePreviewKind, friendlyMimeLabel } from '../../lib/driveFile';
import { DriveIcon } from '../../lib/brandIcons';
import { useI18n } from '../../hooks/useI18n';

interface Props {
  itemId: string;
  mimeType: string | null;
  sizeBytes: number | null;
  fileName: string;
  webViewLink?: string | null;
}

/**
 * Khối preview file Drive trong drawer chi tiết.
 *  - Ảnh nhỏ → tải nội dung gốc hiển thị.
 *  - Ảnh lớn / video / PDF / Google-docs → xin thumbnail từ Google (proxy BE).
 *  - Còn lại (hoặc không có thumbnail) → icon loại + nhãn thân thiện + link mở Drive.
 * Blob lấy qua axios (cookie auth + refresh 401); object URL revoke khi unmount.
 */
export function DriveFilePreview({ itemId, mimeType, sizeBytes, fileName, webViewLink }: Props) {
  const { t } = useI18n();
  const kind = drivePreviewKind(mimeType, sizeBytes);

  const { data: blob, isLoading, isError } = useQuery({
    queryKey: ['drive-preview', itemId, kind],
    queryFn: () =>
      kind === 'image' ? driveApi.fetchContentBlob(itemId) : driveApi.fetchThumbnailBlob(itemId),
    enabled: kind !== 'icon',
    staleTime: 5 * 60_000,
    retry: false,
  });

  // Object URL: PHẢI tạo + revoke trong CÙNG một effect. Nếu tạo bằng useMemo rồi revoke ở effect
  // khác thì StrictMode (dev) chạy mount→cleanup→mount sẽ revoke URL mà memo không tính lại → ảnh vỡ.
  // Mỗi lần effect chạy tạo URL riêng và tự revoke URL đó → an toàn cả StrictMode lẫn khi mở lại drawer.
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    if (!blob) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setUrl(null);
      return;
    }
    const objectUrl = URL.createObjectURL(blob);
    setUrl(objectUrl);
    return () => URL.revokeObjectURL(objectUrl);
  }, [blob]);

  // Lightbox phóng to ảnh — Esc để đóng.
  const [zoomed, setZoomed] = useState(false);
  useEffect(() => {
    if (!zoomed) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setZoomed(false); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [zoomed]);

  const frame =
    'mb-4 rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800/40 overflow-hidden';

  // Đang tải preview (ảnh/thumbnail).
  if (kind !== 'icon' && isLoading) {
    return (
      <div className={`${frame} flex items-center justify-center h-44`}>
        <span className="inline-flex items-center gap-2 text-[12.5px] text-slate-500 dark:text-slate-400">
          <Loader2 className="w-4 h-4 animate-spin text-brand-600 dark:text-brand-400" />
          {t('drive.preview.loading')}
        </span>
      </div>
    );
  }

  // Có ảnh/thumbnail → hiển thị. Bấm để phóng to (lightbox toàn màn hình).
  if (kind !== 'icon' && url && !isError) {
    const alt = t('drive.preview.imageAlt').replace('{name}', fileName);
    return (
      <>
        <div className={frame}>
          <button
            type="button"
            onClick={() => setZoomed(true)}
            title={t('drive.preview.zoom')}
            className="group relative block w-full cursor-zoom-in"
          >
            <img src={url} alt={alt} className="max-h-72 w-full object-contain" />
            <span className="absolute bottom-2 right-2 inline-flex items-center gap-1 rounded-md bg-slate-900/60 px-2 py-1 text-[11px] font-medium text-white opacity-0 group-hover:opacity-100 transition-opacity">
              <ZoomIn className="w-3.5 h-3.5" />
              {t('drive.preview.zoom')}
            </span>
          </button>
        </div>

        {zoomed && (
          <div
            className="fixed inset-0 z-[100] flex items-center justify-center bg-black/80 backdrop-blur-sm p-4 animate-in fade-in duration-150"
            onClick={() => setZoomed(false)}
            role="dialog"
            aria-modal="true"
            aria-label={alt}
          >
            <img
              src={url}
              alt={alt}
              className="max-h-[92vh] max-w-[92vw] object-contain rounded-lg shadow-2xl"
              onClick={(e) => e.stopPropagation()}
            />
            <button
              type="button"
              onClick={() => setZoomed(false)}
              aria-label={t('common.close')}
              className="absolute top-4 right-4 inline-flex items-center justify-center w-10 h-10 rounded-full bg-white/10 text-white hover:bg-white/20 transition-colors"
            >
              <X className="w-5 h-5" />
            </button>
          </div>
        )}
      </>
    );
  }

  // Fallback: icon loại + nhãn thân thiện (+ mở Drive nếu có link).
  return (
    <div className={`${frame} flex flex-col items-center justify-center gap-2 py-8 px-4 text-center`}>
      <div className="w-14 h-14 rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 flex items-center justify-center shadow-sm">
        <DriveIcon className="w-7 h-7" />
      </div>
      <div className="text-[13px] font-medium text-slate-600 dark:text-slate-300">
        {friendlyMimeLabel(mimeType, t)}
      </div>
      {webViewLink ? (
        <a
          href={webViewLink}
          target="_blank"
          rel="noopener noreferrer"
          className="inline-flex items-center gap-1.5 text-[12px] font-medium text-brand-600 dark:text-brand-400 hover:underline"
        >
          <ExternalLink className="w-3.5 h-3.5" />
          {t('drive.preview.openToView')}
        </a>
      ) : (
        <span className="text-[12px] text-slate-400 dark:text-slate-500">{t('drive.preview.unavailable')}</span>
      )}
    </div>
  );
}
