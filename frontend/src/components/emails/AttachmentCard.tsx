import { useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { useQuery } from '@tanstack/react-query';
import { Download, Eye, FileText, Loader2, X } from 'lucide-react';
import { sendEmailApi, type EmailAttachmentDto } from '../../lib/sendEmailApi';
import { useI18n } from '../../hooks/useI18n';

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

interface AttachmentCardProps {
  itemId: string;
  messageId: string;
  att: EmailAttachmentDto;
  onDownload: (att: EmailAttachmentDto) => void;
}

/**
 * Thẻ attachment mức C: ảnh hiển thị thumbnail + click phóng to (lightbox);
 * PDF có nút xem trước (iframe modal); loại khác chỉ tải. Luôn có nút tải xuống.
 */
export function AttachmentCard({ itemId, messageId, att, onDownload }: AttachmentCardProps) {
  const { t } = useI18n();
  const isImage = att.mimeType.startsWith('image/');
  const isPdf = att.mimeType === 'application/pdf';
  const canPreview = isImage || isPdf;

  const [previewOpen, setPreviewOpen] = useState(false);

  // Ảnh: nạp thumbnail ngay. PDF: chỉ nạp binary khi mở preview (tiết kiệm băng thông).
  const { data: blob, isLoading } = useQuery({
    queryKey: ['attachmentBlob', itemId, messageId, att.attachmentId],
    queryFn: () => sendEmailApi.fetchAttachmentBlob(itemId, messageId, att.attachmentId, att.filename, att.mimeType),
    enabled: isImage || (isPdf && previewOpen),
    staleTime: 5 * 60 * 1000,
  });

  const objectUrl = useMemo(() => (blob ? URL.createObjectURL(blob) : null), [blob]);
  useEffect(() => () => { if (objectUrl) URL.revokeObjectURL(objectUrl); }, [objectUrl]);

  return (
    <div className="flex items-center gap-2 pl-2 pr-1.5 py-1.5 border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800 rounded-lg max-w-xs group">
      {/* Thumbnail ảnh / icon */}
      {isImage && objectUrl ? (
        <button
          type="button"
          onClick={() => setPreviewOpen(true)}
          className="shrink-0 w-9 h-9 rounded overflow-hidden border border-slate-200 dark:border-slate-600"
          title={t('attach.preview')}
        >
          <img src={objectUrl} alt={att.filename} className="w-full h-full object-cover" />
        </button>
      ) : isImage && isLoading ? (
        <span className="shrink-0 w-9 h-9 rounded bg-slate-100 dark:bg-slate-700 flex items-center justify-center">
          <Loader2 className="w-3.5 h-3.5 animate-spin text-slate-400" />
        </span>
      ) : (
        <FileText className="w-4 h-4 shrink-0 text-slate-400 dark:text-slate-500" />
      )}

      <div className="min-w-0 flex-1">
        <p className="truncate text-[12.5px] text-slate-700 dark:text-slate-200">{att.filename}</p>
        <p className="text-[11px] text-slate-400 dark:text-slate-500">{formatSize(att.size)}</p>
      </div>

      {canPreview && (
        <button
          type="button"
          onClick={() => setPreviewOpen(true)}
          className="shrink-0 p-1.5 rounded text-slate-400 hover:text-brand-600 dark:text-slate-500 dark:hover:text-brand-400 transition-colors"
          title={t('attach.preview')}
        >
          <Eye className="w-3.5 h-3.5" />
        </button>
      )}
      <button
        type="button"
        onClick={() => onDownload(att)}
        className="shrink-0 p-1.5 rounded text-slate-400 hover:text-brand-600 dark:text-slate-500 dark:hover:text-brand-400 transition-colors"
        title={t('attach.download')}
      >
        <Download className="w-3.5 h-3.5" />
      </button>

      {/* Lightbox / preview modal */}
      {previewOpen && createPortal(
        <div
          className="fixed inset-0 z-[9999] bg-black/70 flex items-center justify-center p-4"
          onClick={() => setPreviewOpen(false)}
        >
          <button
            type="button"
            className="absolute top-4 right-4 p-2 rounded-full bg-white/10 hover:bg-white/20 text-white transition-colors"
            onClick={() => setPreviewOpen(false)}
            aria-label={t('attach.close')}
          >
            <X className="w-5 h-5" />
          </button>

          <div className="max-w-[90vw] max-h-[90vh]" onClick={(e) => e.stopPropagation()}>
            {isImage && objectUrl && (
              <img src={objectUrl} alt={att.filename} className="max-w-[90vw] max-h-[90vh] object-contain rounded-lg shadow-2xl" />
            )}
            {isPdf && (
              objectUrl
                ? <iframe src={objectUrl} title={att.filename} className="w-[90vw] h-[90vh] rounded-lg bg-white shadow-2xl" />
                : <div className="w-[80vw] h-[60vh] flex items-center justify-center text-white"><Loader2 className="w-6 h-6 animate-spin" /></div>
            )}
          </div>
        </div>,
        document.body
      )}
    </div>
  );
}
