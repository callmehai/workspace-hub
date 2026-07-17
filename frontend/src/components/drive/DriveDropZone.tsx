import { useRef, useState, type ReactNode } from 'react';
import { UploadCloud } from 'lucide-react';
import { enqueueFiles, enqueueFolder } from '../../lib/driveUploadStore';
import type { DriveFolderUploadEntry } from '../../types/drive';
import { useI18n } from '../../hooks/useI18n';

interface Props {
  /** Connection Drive active — undefined = không kích hoạt kéo-thả. */
  connectionId?: string;
  /** Folder đích (internalId) — null = My Drive gốc / folder đang mở. */
  parentItemId: string | null;
  className?: string;
  children: ReactNode;
}

/** Đọc hết entries của 1 thư mục (reader trả theo lô → phải gọi lặp tới khi rỗng). */
function readAllEntries(reader: FileSystemDirectoryReader): Promise<FileSystemEntry[]> {
  return new Promise((resolve, reject) => {
    const all: FileSystemEntry[] = [];
    const read = () =>
      reader.readEntries((batch) => {
        if (batch.length === 0) resolve(all);
        else {
          all.push(...batch);
          read();
        }
      }, reject);
    read();
  });
}

function fileFromEntry(entry: FileSystemEntry): Promise<File | null> {
  return new Promise((resolve) => {
    (entry as FileSystemFileEntry).file(
      (f) => resolve(f),
      () => resolve(null),
    );
  });
}

async function walkDir(dir: FileSystemEntry, prefix: string, out: DriveFolderUploadEntry[]) {
  const reader = (dir as FileSystemDirectoryEntry).createReader();
  const children = await readAllEntries(reader);
  for (const child of children) {
    if (child.isFile) {
      const f = await fileFromEntry(child);
      if (f) out.push({ file: f, relativePath: `${prefix}${dir.name}/${f.name}` });
    } else if (child.isDirectory) {
      await walkDir(child, `${prefix}${dir.name}/`, out);
    }
  }
}

/**
 * Vùng kéo-thả upload Drive: thả file → upload song song vào folder đích; thả thư mục → upload cả cây.
 * Chỉ phản ứng với kéo-thả CHỨA FILE (types có 'Files') → không đụng thao tác kéo thẻ Kanban.
 */
export function DriveDropZone({ connectionId, parentItemId, className, children }: Props) {
  const { t } = useI18n();
  const [dragOver, setDragOver] = useState(false);
  const depth = useRef(0); // đếm enter/leave để không nháy khi rê qua phần tử con
  const active = !!connectionId;

  const hasFiles = (e: React.DragEvent) => Array.from(e.dataTransfer.types).includes('Files');

  const onDragEnter = (e: React.DragEvent) => {
    if (!active || !hasFiles(e)) return;
    e.preventDefault();
    depth.current += 1;
    setDragOver(true);
  };
  const onDragOver = (e: React.DragEvent) => {
    if (!active || !hasFiles(e)) return;
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
    if (!active || !hasFiles(e)) return;
    e.preventDefault();
    e.stopPropagation();
    depth.current = 0;
    setDragOver(false);

    const dt = e.dataTransfer;
    const opts = { connectionId: connectionId!, parentItemId };

    // Lấy entries NGAY (đồng bộ) — sau await thì dataTransfer hết hiệu lực.
    const entries = Array.from(dt.items ?? [])
      .map((it) => (it.webkitGetAsEntry ? it.webkitGetAsEntry() : null))
      .filter((x): x is FileSystemEntry => x != null);

    if (entries.length === 0) {
      const files = Array.from(dt.files ?? []);
      if (files.length) enqueueFiles(files, opts);
      return;
    }

    void (async () => {
      const looseFiles: File[] = [];
      for (const entry of entries) {
        if (entry.isFile) {
          const f = await fileFromEntry(entry);
          if (f) looseFiles.push(f);
        } else if (entry.isDirectory) {
          const folderEntries: DriveFolderUploadEntry[] = [];
          await walkDir(entry, '', folderEntries);
          if (folderEntries.length) enqueueFolder(entry.name, folderEntries, opts);
        }
      }
      if (looseFiles.length) enqueueFiles(looseFiles, opts);
    })();
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
