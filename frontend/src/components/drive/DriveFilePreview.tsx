import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { ExternalLink, Loader2 } from 'lucide-react';
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

  // Có ảnh/thumbnail → hiển thị.
  if (kind !== 'icon' && url && !isError) {
    return (
      <div className={`${frame} flex items-center justify-center`}>
        <img
          src={url}
          alt={t('drive.preview.imageAlt').replace('{name}', fileName)}
          className="max-h-72 w-full object-contain"
        />
      </div>
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
