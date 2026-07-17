import { FileText, Folder, CheckCircle2, AlertCircle, X, Loader2 } from 'lucide-react';
import { useDriveUploads } from '../../hooks/useDriveUploads';
import { clearFinished, dismissTask, type UploadTask } from '../../lib/driveUploadStore';
import { useI18n } from '../../hooks/useI18n';
import type { TranslationKey } from '../../i18n/translations';

/**
 * Panel tiến độ upload Drive — nổi góc dưới-phải, mount 1 lần ở MainLayout nên sống xuyên trang.
 * Mỗi file 1 dòng: progress bar khi đang tải · check khi xong · lý do khi bỏ qua/lỗi.
 */
export function DriveUploadPanel() {
  const { t } = useI18n();
  const tasks = useDriveUploads();

  if (tasks.length === 0) return null;

  const uploading = tasks.filter((x) => x.status === 'uploading').length;
  const allDone = uploading === 0;

  return (
    <div className="fixed bottom-4 right-4 z-[80] w-[340px] max-w-[calc(100vw-2rem)] rounded-xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 shadow-2xl overflow-hidden">
      <div className="flex items-center justify-between px-3.5 py-2.5 border-b border-slate-100 dark:border-slate-800">
        <div className="flex items-center gap-2 text-[13px] font-semibold text-slate-800 dark:text-slate-100">
          {uploading > 0 ? (
            <Loader2 className="w-4 h-4 animate-spin text-brand-600 dark:text-brand-400" />
          ) : (
            <CheckCircle2 className="w-4 h-4 text-emerald-500" />
          )}
          <span>
            {uploading > 0
              ? t('drive.uploadPanel.uploading').replace('{n}', String(uploading))
              : t('drive.uploadPanel.done')}
          </span>
        </div>
        <button
          type="button"
          onClick={clearFinished}
          disabled={!allDone && uploading === tasks.length}
          className="text-[12px] font-medium text-slate-500 hover:text-slate-700 dark:text-slate-400 dark:hover:text-slate-200 disabled:opacity-40"
        >
          {t('drive.uploadPanel.clear')}
        </button>
      </div>

      <div className="max-h-72 overflow-y-auto py-1">
        {tasks.map((task) => (
          <UploadRow key={task.id} task={task} label={rowLabel(task, t)} />
        ))}
      </div>
    </div>
  );
}

function rowLabel(task: UploadTask, t: (k: TranslationKey) => string): string {
  if (task.status === 'skipped' && task.errorKey) return t(task.errorKey);
  if (task.status === 'error') return task.detail ? `${t('drive.uploadPanel.failed')} · ${task.detail}` : t('drive.uploadPanel.failed');
  if (task.status === 'done') return task.detail ?? t('drive.uploadPanel.uploaded');
  return `${task.progress}%`;
}

function UploadRow({ task, label }: { task: UploadTask; label: string }) {
  const isBad = task.status === 'error' || task.status === 'skipped';
  const Icon = task.kind === 'folder' ? Folder : FileText;

  return (
    <div className="flex items-center gap-2.5 px-3.5 py-2">
      <span className="shrink-0 text-slate-400 dark:text-slate-500">
        <Icon className="w-4 h-4" />
      </span>
      <div className="flex-1 min-w-0">
        <div className="flex items-center justify-between gap-2">
          <span className="text-[12.5px] font-medium text-slate-700 dark:text-slate-200 truncate">{task.name}</span>
          <span
            className={`text-[11px] shrink-0 ${
              task.status === 'done'
                ? 'text-emerald-600 dark:text-emerald-400'
                : isBad
                  ? 'text-amber-600 dark:text-amber-400'
                  : 'text-slate-400 dark:text-slate-500 tabular-nums'
            }`}
          >
            {label}
          </span>
        </div>
        {task.status === 'uploading' && (
          <div className="mt-1 h-1 rounded-full bg-slate-100 dark:bg-slate-800 overflow-hidden">
            <div
              className="h-full bg-brand-500 transition-[width] duration-200"
              style={{ width: `${task.progress}%` }}
            />
          </div>
        )}
      </div>
      {task.status !== 'uploading' ? (
        <button
          type="button"
          onClick={() => dismissTask(task.id)}
          className="shrink-0 p-0.5 rounded text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
          aria-label="dismiss"
        >
          {task.status === 'error' ? <AlertCircle className="w-4 h-4 text-rose-500" /> : <X className="w-3.5 h-3.5" />}
        </button>
      ) : (
        <span className="w-4 shrink-0" />
      )}
    </div>
  );
}
