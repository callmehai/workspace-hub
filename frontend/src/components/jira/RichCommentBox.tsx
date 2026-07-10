/**
 * RichCommentBox — soạn comment kiểu "như mail": thanh công cụ định dạng (đậm/nghiêng/gạch/list/link)
 * + đính kèm file. Xuất ra markdown subset (khớp AdfConverter BE). Dùng cho cả thêm mới & sửa.
 */
import { useRef, useState } from 'react';
import { Bold, Italic, Strikethrough, List, ListOrdered, Link2, Paperclip, Loader2, Send, Eye, X, Heading2, Code } from 'lucide-react';
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

/** Nút toolbar — onMouseDown preventDefault để không mất selection trong textarea. Style khớp RichTextEditor (email). */
function Tool({ onClick, title, active, disabled, children }: { onClick: () => void; title: string; active?: boolean; disabled?: boolean; children: React.ReactNode }) {
  return (
    <button
      type="button" onMouseDown={(e) => e.preventDefault()} onClick={onClick} title={title} disabled={disabled}
      className={`w-8 h-8 flex items-center justify-center rounded-md transition-colors disabled:opacity-40 disabled:cursor-not-allowed ${
        active
          ? 'bg-brand-100 text-brand-700 dark:bg-brand-500/20 dark:text-brand-300'
          : 'text-slate-600 hover:bg-slate-200 dark:text-slate-400 dark:hover:bg-slate-700'
      }`}
    >
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
    <div className="rounded-xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 overflow-hidden shadow-sm focus-within:ring-2 focus-within:ring-brand-500/20 focus-within:border-brand-500 transition-colors">
      {/* Toolbar — style khớp editor email */}
      <div className="flex items-center flex-wrap gap-0.5 px-2 py-1.5 border-b border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800">
        <Tool onClick={() => surround('**')} title={t('ticket.rtBold')} disabled={preview}><Bold className="w-4 h-4" /></Tool>
        <Tool onClick={() => surround('*')} title={t('ticket.rtItalic')} disabled={preview}><Italic className="w-4 h-4" /></Tool>
        <Tool onClick={() => surround('~~')} title={t('ticket.rtStrike')} disabled={preview}><Strikethrough className="w-4 h-4" /></Tool>
        <Tool onClick={() => surround('`')} title={t('ticket.rtCode')} disabled={preview}><Code className="w-4 h-4" /></Tool>
        <span className="w-px h-5 bg-slate-200 dark:bg-slate-700 mx-1" />
        <Tool onClick={() => linePrefix('## ')} title={t('ticket.rtHeading')} disabled={preview}><Heading2 className="w-4 h-4" /></Tool>
        <Tool onClick={() => linePrefix('- ')} title={t('ticket.rtBullet')} disabled={preview}><List className="w-4 h-4" /></Tool>
        <Tool onClick={() => linePrefix('1. ')} title={t('ticket.rtOrdered')} disabled={preview}><ListOrdered className="w-4 h-4" /></Tool>
        <Tool onClick={() => surround('[', '](https://)')} title={t('ticket.rtLink')} disabled={preview}><Link2 className="w-4 h-4" /></Tool>
        {allowAttach && <>
          <span className="w-px h-5 bg-slate-200 dark:bg-slate-700 mx-1" />
          <Tool onClick={() => fileRef.current?.click()} title={t('ticket.attachmentUpload')} disabled={preview}><Paperclip className="w-4 h-4" /></Tool>
        </>}
        <span className="flex-1" />
        {/* Toggle xem trước — pill có nhãn, rõ trạng thái bật/tắt */}
        <button
          type="button"
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => setPreview((p) => !p)}
          className={`inline-flex items-center gap-1.5 h-7 px-2.5 rounded-full text-[11.5px] font-semibold transition-colors ${
            preview
              ? 'bg-brand-600 text-white shadow-sm'
              : 'text-slate-500 dark:text-slate-400 hover:bg-slate-200 dark:hover:bg-slate-700'
          }`}
        >
          <Eye className="w-3.5 h-3.5" />{t('ticket.rtPreview')}
        </button>
        <input ref={fileRef} type="file" multiple className="hidden" onChange={onPick} />
      </div>

      {/* Editor / Preview */}
      {preview ? (
        <div className="px-3.5 py-2.5 text-[13.5px] text-slate-800 dark:text-slate-100 leading-[1.65] min-h-[72px] max-h-60 overflow-y-auto bg-slate-50/50 dark:bg-slate-800/40">
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
          className="w-full px-3.5 py-2.5 text-[13.5px] leading-[1.6] text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 bg-transparent focus:outline-none resize-y min-h-[72px]"
        />
      )}

      {/* File chips */}
      {files.length > 0 && (
        <div className="flex flex-wrap gap-1.5 px-3.5 pb-2.5">
          {files.map((f, idx) => (
            <span key={idx} className="inline-flex items-center gap-1.5 pl-2.5 pr-1 py-1 rounded-lg border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800 text-[11.5px] text-slate-700 dark:text-slate-200">
              <Paperclip className="w-3 h-3 text-slate-400" /><span className="max-w-[160px] truncate">{f.name}</span>
              <button onClick={() => setFiles((prev) => prev.filter((_, i) => i !== idx))} className="p-0.5 rounded-full text-slate-400 hover:text-slate-600 hover:bg-slate-200 dark:hover:bg-slate-600 transition-colors"><X className="w-3 h-3" /></button>
            </span>
          ))}
        </div>
      )}

      {/* Actions */}
      <div className="flex items-center justify-end gap-2 px-3 py-2 border-t border-slate-100 dark:border-slate-700/60 bg-slate-50/60 dark:bg-slate-800/40">
        {onCancel && (
          <button onClick={onCancel} className="h-8 px-3 rounded-lg text-[12.5px] font-medium text-slate-600 dark:text-slate-300 hover:bg-slate-200/70 dark:hover:bg-slate-700 transition-colors">{t('common.cancel')}</button>
        )}
        <button
          onClick={submit}
          disabled={pending || (!value.trim() && files.length === 0)}
          className="inline-flex items-center gap-1.5 h-8 px-3.5 bg-brand-600 text-white rounded-lg text-[12.5px] font-semibold hover:bg-brand-700 disabled:opacity-60 shadow-sm transition-colors"
        >
          {pending ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Send className="w-3.5 h-3.5" />}{submitLabel}
        </button>
      </div>
    </div>
  );
}
