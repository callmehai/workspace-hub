/**
 * RichCommentBox — soạn comment kiểu "như mail": thanh công cụ định dạng (đậm/nghiêng/gạch/list/link)
 * + đính kèm file. Xuất ra markdown subset (khớp AdfConverter BE). Dùng cho cả thêm mới & sửa.
 */
import { useRef, useState } from 'react';
import { Bold, Italic, Strikethrough, List, ListOrdered, Link2, Paperclip, Loader2, Send, Eye, X } from 'lucide-react';
import type { useI18n } from '../../hooks/useI18n';
import { renderRichText } from './miniMarkdown';

type TFn = ReturnType<typeof useI18n>['t'];

interface Props {
  initialValue?: string;
  placeholder: string;
  submitLabel: string;
  allowAttach?: boolean;
  pending: boolean;
  t: TFn;
  onSubmit: (body: string, files: File[]) => Promise<unknown> | void;
  onCancel?: () => void;
}

const MAX = 25 * 1024 * 1024;

/** Nút toolbar — onMouseDown preventDefault để không mất selection trong textarea. */
function Tool({ onClick, title, children }: { onClick: () => void; title: string; children: React.ReactNode }) {
  return (
    <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={onClick} title={title}
      className="p-1.5 rounded text-slate-500 dark:text-slate-400 hover:text-brand-600 hover:bg-white dark:hover:bg-slate-700 transition-colors">
      {children}
    </button>
  );
}

export function RichCommentBox({ initialValue = '', placeholder, submitLabel, allowAttach, pending, t, onSubmit, onCancel }: Props) {
  const [value, setValue] = useState(initialValue);
  const [files, setFiles] = useState<File[]>([]);
  const [preview, setPreview] = useState(false);
  const taRef = useRef<HTMLTextAreaElement>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  const surround = (before: string, after = before) => {
    const ta = taRef.current; if (!ta) return;
    const s = ta.selectionStart, e = ta.selectionEnd;
    const sel = value.slice(s, e);
    const next = value.slice(0, s) + before + sel + after + value.slice(e);
    setValue(next);
    requestAnimationFrame(() => { ta.focus(); ta.selectionStart = s + before.length; ta.selectionEnd = s + before.length + sel.length; });
  };
  const linePrefix = (prefix: string) => {
    const ta = taRef.current; if (!ta) return;
    const s = ta.selectionStart, e = ta.selectionEnd;
    const startLine = value.lastIndexOf('\n', s - 1) + 1;
    const block = value.slice(startLine, e) || '';
    const prefixed = block.split('\n').map((l) => prefix + l).join('\n');
    const next = value.slice(0, startLine) + prefixed + value.slice(e);
    setValue(next);
    requestAnimationFrame(() => ta.focus());
  };

  const onPick = (e: React.ChangeEvent<HTMLInputElement>) => {
    const picked = Array.from(e.target.files ?? []);
    e.target.value = '';
    const ok = picked.filter((f) => f.size <= MAX);
    if (ok.length < picked.length) alert(t('ticket.attachmentTooBig'));
    setFiles((prev) => [...prev, ...ok]);
  };

  const submit = async () => {
    if (!value.trim() && files.length === 0) return;
    await onSubmit(value.trim(), files);
    setValue(''); setFiles([]); setPreview(false);
  };

  return (
    <div className="rounded-lg border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-800 overflow-hidden">
      {/* Toolbar */}
      <div className="flex items-center gap-0.5 px-1.5 py-1 border-b border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800/80 flex-wrap">
        <Tool onClick={() => surround('**')} title={t('ticket.rtBold')}><Bold className="w-4 h-4" /></Tool>
        <Tool onClick={() => surround('*')} title={t('ticket.rtItalic')}><Italic className="w-4 h-4" /></Tool>
        <Tool onClick={() => surround('~~')} title={t('ticket.rtStrike')}><Strikethrough className="w-4 h-4" /></Tool>
        <span className="w-px h-4 bg-slate-200 dark:bg-slate-700 mx-0.5" />
        <Tool onClick={() => linePrefix('- ')} title={t('ticket.rtBullet')}><List className="w-4 h-4" /></Tool>
        <Tool onClick={() => linePrefix('1. ')} title={t('ticket.rtOrdered')}><ListOrdered className="w-4 h-4" /></Tool>
        <Tool onClick={() => surround('[', '](https://)')} title={t('ticket.rtLink')}><Link2 className="w-4 h-4" /></Tool>
        {allowAttach && <>
          <span className="w-px h-4 bg-slate-200 dark:bg-slate-700 mx-0.5" />
          <Tool onClick={() => fileRef.current?.click()} title={t('ticket.attachmentUpload')}><Paperclip className="w-4 h-4" /></Tool>
        </>}
        <span className="flex-1" />
        <Tool onClick={() => setPreview((p) => !p)} title={t('ticket.rtPreview')}>
          <Eye className={`w-4 h-4 ${preview ? 'text-brand-600' : ''}`} />
        </Tool>
        <input ref={fileRef} type="file" multiple className="hidden" onChange={onPick} />
      </div>

      {/* Editor / Preview */}
      {preview ? (
        <div className="px-3 py-2 text-[13px] text-slate-800 dark:text-slate-100 leading-[1.55] min-h-[56px] max-h-60 overflow-y-auto">
          {value.trim()
            ? renderRichText(value)
            : <span className="text-slate-400 italic">{t('ticket.rtPreviewEmpty')}</span>}
        </div>
      ) : (
        <textarea
          ref={taRef}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          placeholder={placeholder}
          rows={3}
          className="w-full px-3 py-2 text-[13px] text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 bg-transparent focus:outline-none resize-y min-h-[56px]"
        />
      )}

      {/* File chips */}
      {files.length > 0 && (
        <div className="flex flex-wrap gap-1.5 px-3 pb-2">
          {files.map((f, idx) => (
            <span key={idx} className="inline-flex items-center gap-1 pl-2 pr-1 py-0.5 rounded-full bg-slate-100 dark:bg-slate-700 text-[11.5px] text-slate-700 dark:text-slate-200">
              <Paperclip className="w-3 h-3 text-slate-400" />{f.name}
              <button onClick={() => setFiles((prev) => prev.filter((_, i) => i !== idx))} className="p-0.5 rounded-full hover:bg-slate-200 dark:hover:bg-slate-600"><X className="w-3 h-3" /></button>
            </span>
          ))}
        </div>
      )}

      {/* Actions */}
      <div className="flex items-center justify-end gap-2 px-2.5 py-1.5 border-t border-slate-100 dark:border-slate-700/60">
        {onCancel && (
          <button onClick={onCancel} className="px-2.5 py-1 border border-slate-200 dark:border-slate-700 rounded-lg text-[11.5px] font-medium text-slate-600 dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-700 transition-colors">{t('common.cancel')}</button>
        )}
        <button
          onClick={submit}
          disabled={pending || (!value.trim() && files.length === 0)}
          className="inline-flex items-center gap-1.5 px-3 py-1 bg-brand-600 text-white rounded-lg text-xs font-semibold hover:bg-brand-700 disabled:opacity-60 transition-colors"
        >
          {pending ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Send className="w-3.5 h-3.5" />}{submitLabel}
        </button>
      </div>
    </div>
  );
}
