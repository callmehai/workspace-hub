import { useRef, useState, type ReactNode } from 'react';
import { UploadCloud } from 'lucide-react';
import { dragHasFiles, handleDriveDrop } from '../../lib/driveDrop';
import { useI18n } from '../../hooks/useI18n';

interface Props {
  /** Connection Drive active — undefined = không kích hoạt kéo-thả. */
  connectionId?: string;
  /** Folder Drive đích (internalId) — null = My Drive gốc / folder đang mở. */
  parentItemId: string | null;
  /** Folder CONTEXT app đang xem — gán item mới vào (null = "Tất cả mục", không gán). */
  folderId?: string | null;
  className?: string;
  children: ReactNode;
}

/**
 * Vùng kéo-thả upload Drive: thả file → upload song song vào folder đích; thả thư mục → upload cả cây.
 * Chỉ phản ứng với kéo-thả CHỨA FILE (types có 'Files') → không đụng thao tác kéo thẻ Kanban.
 * Nếu đang đứng trong 1 folder context app (folderId), item mới cũng được gán vào folder đó.
 */
export function DriveDropZone({ connectionId, parentItemId, folderId, className, children }: Props) {
  const { t } = useI18n();
  const [dragOver, setDragOver] = useState(false);
  const depth = useRef(0); // đếm enter/leave để không nháy khi rê qua phần tử con
  const active = !!connectionId;

  const onDragEnter = (e: React.DragEvent) => {
    if (!active || !dragHasFiles(e)) return;
    e.preventDefault();
    depth.current += 1;
    setDragOver(true);
  };
  const onDragOver = (e: React.DragEvent) => {
    if (!active || !dragHasFiles(e)) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = 'copy';
  };
  const onDragLeave = () => {
    if (!active) return;
    depth.current -= 1;
    if (depth.current <= 0) {
      depth.current = 0;
      setDragOver(false);
    }
  };
  const onDrop = (e: React.DragEvent) => {
    if (!active || !dragHasFiles(e)) return;
    e.preventDefault();
    e.stopPropagation();
    depth.current = 0;
    setDragOver(false);
    handleDriveDrop(e.dataTransfer, { connectionId: connectionId!, parentItemId, folderId });
  };

  return (
    <div
      className={`relative ${className ?? ''}`}
      onDragEnter={onDragEnter}
      onDragOver={onDragOver}
      onDragLeave={onDragLeave}
      onDrop={onDrop}
    >
      {children}
      {dragOver && (
        <div className="pointer-events-none absolute inset-0 z-30 rounded-xl border-2 border-dashed border-brand-400 bg-brand-50/80 dark:bg-brand-500/15 backdrop-blur-[1px] flex items-center justify-center">
          <div className="flex flex-col items-center gap-2 text-brand-700 dark:text-brand-300">
            <UploadCloud className="w-8 h-8" />
            <span className="text-sm font-semibold">{t('drive.drop.hint')}</span>
          </div>
        </div>
      )}
    </div>
  );
}
