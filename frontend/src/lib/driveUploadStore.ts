import { driveApi } from './driveApi';
import { MAX_DRIVE_FILE_BYTES } from '../types/drive';
import type { DriveFolderUploadEntry } from '../types/drive';
import type { TranslationKey } from '../i18n/translations';

/**
 * Store toàn cục cho hàng đợi upload Drive — dùng chung cho nút "Mới" và kéo-thả.
 * KHÔNG phụ thuộc React (module singleton + pub/sub) → panel tiến độ mount 1 lần ở MainLayout,
 * sống xuyên trang. Upload nhiều file SONG SONG (giới hạn CONCURRENCY), mỗi file 1 progress bar,
 * file lỗi (rỗng / quá 100MB) bị BỎ QUA với lý do, không chặn cả lô.
 */

export type UploadStatus = 'uploading' | 'done' | 'error' | 'skipped';

export interface UploadTask {
  id: string;
  name: string;
  kind: 'file' | 'folder';
  progress: number; // 0–100
  status: UploadStatus;
  /** Lý do i18n khi skipped/error (nếu có). */
  errorKey?: TranslationKey;
  /** Thông tin thêm (vd kết quả upload folder). */
  detail?: string;
}

type Listener = () => void;

const CONCURRENCY = 3;
const listeners = new Set<Listener>();
const queue: Array<() => Promise<void>> = [];

let tasks: UploadTask[] = [];
let seq = 0;
let active = 0;

function emit() {
  for (const l of listeners) l();
}

function setTasks(next: UploadTask[]) {
  tasks = next;
  emit();
}

function patchTask(id: string, patch: Partial<UploadTask>) {
  setTasks(tasks.map((t) => (t.id === id ? { ...t, ...patch } : t)));
}

/** Kéo job từ queue chạy tới khi đủ CONCURRENCY luồng. */
function pump() {
  while (active < CONCURRENCY && queue.length > 0) {
    const job = queue.shift()!;
    active += 1;
    void job().finally(() => {
      active -= 1;
      pump();
    });
  }
}

/** Validate 1 file trước khi upload — trả key i18n lý do bỏ qua, hoặc null nếu hợp lệ. */
function validateFile(file: File): TranslationKey | null {
  if (file.size <= 0) return 'drive.upload.emptyFile';
  if (file.size > MAX_DRIVE_FILE_BYTES) return 'drive.upload.fileTooBig';
  return null;
}

export interface EnqueueOpts {
  connectionId: string;
  parentItemId: string | null;
}

/** Thêm nhiều file vào hàng đợi (song song). File lỗi → task 'skipped' kèm lý do. */
export function enqueueFiles(files: File[], opts: EnqueueOpts) {
  for (const file of files) {
    const id = `u${(seq += 1)}`;
    const errorKey = validateFile(file);

    if (errorKey) {
      setTasks([...tasks, { id, name: file.name, kind: 'file', progress: 0, status: 'skipped', errorKey }]);
      continue;
    }

    setTasks([...tasks, { id, name: file.name, kind: 'file', progress: 0, status: 'uploading' }]);
    queue.push(async () => {
      try {
        await driveApi.uploadFile(
          { connectionId: opts.connectionId, file, parentItemId: opts.parentItemId },
          (percent) => patchTask(id, { progress: percent }),
        );
        patchTask(id, { status: 'done', progress: 100 });
      } catch {
        patchTask(id, { status: 'error' });
      }
    });
  }
  pump();
}

/** Thêm 1 folder (upload batch) — hiện 1 task với progress tổng, phản ánh số file lỗi. */
export function enqueueFolder(folderName: string, entries: DriveFolderUploadEntry[], opts: EnqueueOpts) {
  const id = `u${(seq += 1)}`;
  setTasks([...tasks, { id, name: folderName, kind: 'folder', progress: 0, status: 'uploading' }]);

  queue.push(async () => {
    try {
      const result = await driveApi.uploadFolder(
        { connectionId: opts.connectionId, entries, parentItemId: opts.parentItemId },
        (percent) => patchTask(id, { progress: percent }),
      );
      const failed = result.failed?.length ?? 0;
      patchTask(id, {
        status: failed > 0 ? 'error' : 'done',
        progress: 100,
        detail: `${result.filesUploaded}/${result.filesUploaded + failed}`,
      });
    } catch {
      patchTask(id, { status: 'error' });
    }
  });
  pump();
}

export function subscribe(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function getSnapshot(): UploadTask[] {
  return tasks;
}

export function dismissTask(id: string) {
  setTasks(tasks.filter((t) => t.id !== id));
}

/** Xoá các task đã xong/lỗi/bỏ qua khỏi panel (giữ lại đang chạy). */
export function clearFinished() {
  setTasks(tasks.filter((t) => t.status === 'uploading'));
}
