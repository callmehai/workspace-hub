import type { DragEvent } from 'react';
import { enqueueFiles, enqueueFolder, type EnqueueOpts } from './driveUploadStore';
import type { DriveFolderUploadEntry } from '../types/drive';

/**
 * Logic kéo-thả upload Drive dùng chung — cho cả vùng thả tổng (DriveDropZone) lẫn từng hàng
 * folder (thả lên folder cụ thể → upload thẳng vào folder đó). Tách khỏi component để không lặp code
 * đọc entries / đệ quy cây thư mục.
 */

/** Kéo-thả này có chứa FILE không? (types có 'Files' → phân biệt với kéo thẻ Kanban.) */
export function dragHasFiles(e: DragEvent): boolean {
  return Array.from(e.dataTransfer.types).includes('Files');
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
 * Xử lý `dataTransfer` của 1 drop chứa file → đẩy vào hàng đợi upload với `opts` (connection + folder đích).
 * PHẢI đọc entries NGAY (đồng bộ) trước mọi await — `dataTransfer` hết hiệu lực sau await.
 */
export function handleDriveDrop(dt: DataTransfer, opts: EnqueueOpts): void {
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
}
