import { useRef } from 'react';
import { Paperclip, X, FileText } from 'lucide-react';
import { MAX_ATTACHMENT_TOTAL_BYTES } from '../lib/sendEmailApi';
import { useI18n } from '../hooks/useI18n';

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

interface AttachmentPickerProps {
  files: File[];
  onChange: (files: File[]) => void;
  className?: string;
}

/**
 * Chọn / hiển thị / gỡ file đính kèm (dùng chung cho compose / reply / forward).
 * Controlled: cha giữ File[]; lúc gửi cha convert sang base64 qua fileToAttachmentUpload.
 */
export function AttachmentPicker({ files, onChange, className }: AttachmentPickerProps) {
  const { t } = useI18n();
  const inputRef = useRef<HTMLInputElement>(null);

  const total = files.reduce((sum, f) => sum + f.size, 0);
  const over = total > MAX_ATTACHMENT_TOTAL_BYTES;

  const fileKey = (f: File) => `${f.name}::${f.size}`;

  const addFiles = (list: FileList | null) => {
    if (!list || list.length === 0) return;
    const existing = new Set(files.map(fileKey));
    const merged = [...files];
    for (const f of Array.from(list)) {
      if (!existing.has(fileKey(f))) merged.push(f);
    }
    onChange(merged);
    if (inputRef.current) inputRef.current.value = '';
  };

  const remove = (idx: number) => onChange(files.filter((_, i) => i !== idx));

  return (
    <div className={className}>
      <input ref={inputRef} type="file" multiple className="hidden" onChange={(e) => addFiles(e.target.files)} />

      <button
        type="button"
        onClick={() => inputRef.current?.click()}
        className="inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-[13px] font-medium border border-gray-300 dark:border-slate-700 text-gray-600 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-800 hover:text-gray-800 dark:hover:text-slate-100 transition-colors"
      >
        <Paperclip className="w-3.5 h-3.5" />
        {t('attach.add')}
      </button>

      {files.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-2">
          {files.map((f, i) => (
            <span
              key={fileKey(f)}
              className="inline-flex items-center gap-1.5 max-w-[220px] pl-2 pr-1 py-1 rounded-lg border border-gray-200 dark:border-slate-700 bg-gray-50 dark:bg-slate-800 text-[12px]"
            >
              <FileText className="w-3.5 h-3.5 shrink-0 text-gray-400 dark:text-slate-500" />
              <span className="truncate text-gray-700 dark:text-slate-200">{f.name}</span>
              <span className="shrink-0 text-gray-400 dark:text-slate-500">{formatFileSize(f.size)}</span>
              <button
                type="button"
                onClick={() => remove(i)}
                className="shrink-0 p-0.5 rounded text-gray-400 hover:text-red-500 dark:text-slate-500 dark:hover:text-red-400 transition-colors"
                aria-label={t('attach.remove')}
              >
                <X className="w-3.5 h-3.5" />
              </button>
            </span>
          ))}
        </div>
      )}

      {over && (
        <p className="mt-1.5 text-[12px] text-red-500 dark:text-red-400">{t('attach.tooLarge')}</p>
      )}
    </div>
  );
}
