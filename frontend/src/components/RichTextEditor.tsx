import { useRef, useState, useEffect } from 'react';
import { createPortal } from 'react-dom';
import {
  Bold, Italic, Underline, Strikethrough, List, ListOrdered,
  Link2, Heading2, AlignLeft, AlignCenter, Eraser, Code,
  Maximize2, Minimize2, Paperclip, X, FileText,
} from 'lucide-react';
import './RichTextEditor.css';
import { useI18n } from '../hooks/useI18n';
import { MAX_ATTACHMENT_TOTAL_BYTES } from '../lib/sendEmailApi';
import type { TranslationKey } from '../i18n/translations';

interface RichTextEditorProps {
  value: string;
  onChange: (html: string) => void;
  placeholder?: string;
  className?: string;
  /** Đính kèm tệp (controlled). Truyền cả 2 để bật nút 📎 trong toolbar + chips ở đáy editor. */
  attachFiles?: File[];
  onAttachFilesChange?: (files: File[]) => void;
}

interface ToolButton {
  icon: React.ComponentType<{ className?: string }>;
  titleKey: TranslationKey;
  cmd: string;
  arg?: string;
}

const TOOLS: ToolButton[][] = [
  [
    { icon: Bold, titleKey: 'editor.bold', cmd: 'bold' },
    { icon: Italic, titleKey: 'editor.italic', cmd: 'italic' },
    { icon: Underline, titleKey: 'editor.underline', cmd: 'underline' },
    { icon: Strikethrough, titleKey: 'editor.strike', cmd: 'strikeThrough' },
  ],
  [
    { icon: Heading2, titleKey: 'editor.heading', cmd: 'formatBlock', arg: 'H2' },
    { icon: List, titleKey: 'editor.bullet', cmd: 'insertUnorderedList' },
    { icon: ListOrdered, titleKey: 'editor.ordered', cmd: 'insertOrderedList' },
  ],
  [
    { icon: AlignLeft, titleKey: 'editor.alignLeft', cmd: 'justifyLeft' },
    { icon: AlignCenter, titleKey: 'editor.alignCenter', cmd: 'justifyCenter' },
  ],
];

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

const escapeHtml = (s: string) =>
  s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

/** Trình soạn thảo rich text (WYSIWYG) → HTML, kèm toggle HTML thô + phóng to toàn màn hình. */
export function RichTextEditor({
  value, onChange, placeholder, className, attachFiles, onAttachFilesChange,
}: RichTextEditorProps) {
  const { t } = useI18n();
  const editorRef = useRef<HTMLDivElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const linkPopRef = useRef<HTMLDivElement>(null);
  const savedRange = useRef<Range | null>(null);
  const [htmlMode, setHtmlMode] = useState(false);
  const [expanded, setExpanded] = useState(false);

  const [linkOpen, setLinkOpen] = useState(false);
  const [linkUrl, setLinkUrl] = useState('https://');
  const [linkText, setLinkText] = useState('');
  const [linkHasSelection, setLinkHasSelection] = useState(false);

  const attachEnabled = !!onAttachFilesChange;
  const files = attachFiles ?? [];
  const totalSize = files.reduce((sum, f) => sum + f.size, 0);
  const overLimit = totalSize > MAX_ATTACHMENT_TOTAL_BYTES;

  // Đồng bộ value bên ngoài vào editor (chỉ khi khác) để tránh nhảy con trỏ.
  // Có `expanded` trong deps để re-sync innerHTML sau khi remount lúc phóng to/thu nhỏ.
  useEffect(() => {
    if (!htmlMode && editorRef.current && editorRef.current.innerHTML !== value) {
      editorRef.current.innerHTML = value || '';
    }
  }, [value, htmlMode, expanded]);

  useEffect(() => {
    if (!expanded) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setExpanded(false); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [expanded]);

  // Đóng popover link khi click ra ngoài
  useEffect(() => {
    if (!linkOpen) return;
    const onDoc = (e: MouseEvent) => {
      if (linkPopRef.current && !linkPopRef.current.contains(e.target as Node)) setLinkOpen(false);
    };
    document.addEventListener('mousedown', onDoc);
    return () => document.removeEventListener('mousedown', onDoc);
  }, [linkOpen]);

  const exec = (cmd: string, arg?: string) => {
    editorRef.current?.focus();
    document.execCommand(cmd, false, arg);
    onChange(editorRef.current?.innerHTML ?? '');
  };

  // ── Link: popover in-app (thay window.prompt) ──
  const openLinkPopover = () => {
    const sel = window.getSelection();
    if (sel && sel.rangeCount > 0 && editorRef.current?.contains(sel.anchorNode)) {
      const range = sel.getRangeAt(0).cloneRange();
      savedRange.current = range;
      setLinkText(sel.toString());
      setLinkHasSelection(!range.collapsed);
    } else {
      savedRange.current = null;
      setLinkText('');
      setLinkHasSelection(false);
    }
    setLinkUrl('https://');
    setLinkOpen(true);
  };

  const applyLink = () => {
    const url = linkUrl.trim();
    if (!url || url === 'https://') return;
    editorRef.current?.focus();
    const range = savedRange.current;
    const sel = window.getSelection();
    if (range && sel) { sel.removeAllRanges(); sel.addRange(range); }

    if (!range || range.collapsed) {
      const label = escapeHtml(linkText.trim() || url);
      document.execCommand('insertHTML', false,
        `<a href="${escapeHtml(url)}" target="_blank" rel="noopener noreferrer">${label}</a>`);
    } else {
      document.execCommand('createLink', false, url);
    }
    onChange(editorRef.current?.innerHTML ?? '');
    setLinkOpen(false);
  };

  const clearFormat = () => {
    exec('removeFormat');
    exec('formatBlock', 'DIV');
  };

  // ── Attach ──
  const fileKey = (f: File) => `${f.name}::${f.size}`;
  const addFiles = (list: FileList | null) => {
    if (!onAttachFilesChange || !list || list.length === 0) return;
    const existing = new Set(files.map(fileKey));
    const merged = [...files];
    for (const f of Array.from(list)) if (!existing.has(fileKey(f))) merged.push(f);
    onAttachFilesChange(merged);
    if (fileInputRef.current) fileInputRef.current.value = '';
  };
  const removeFile = (idx: number) => onAttachFilesChange?.(files.filter((_, i) => i !== idx));

  const btnClass = 'w-8 h-8 flex items-center justify-center rounded-md text-gray-600 hover:bg-gray-200 dark:text-slate-400 dark:hover:bg-slate-700 transition-colors';
  const disCls = 'disabled:opacity-40 disabled:pointer-events-none';

  const editor = (
    <div className={`border border-gray-300 dark:border-slate-700 rounded-lg overflow-hidden bg-white dark:bg-slate-900 flex flex-col focus-within:ring-2 focus-within:ring-brand-500/20 focus-within:border-brand-500 transition-colors ${expanded ? 'h-full' : (className ?? '')}`}>
      {/* Toolbar (dính trên cùng) */}
      <div className="flex items-center flex-wrap gap-0.5 px-2 py-1.5 border-b border-gray-200 dark:border-slate-700 bg-gray-50 dark:bg-slate-800 shrink-0">
        {TOOLS.map((group, gi) => (
          <div key={gi} className="flex items-center gap-0.5">
            {group.map((tb) => (
              <button key={tb.titleKey} type="button" title={t(tb.titleKey)} onMouseDown={(e) => e.preventDefault()} onClick={() => exec(tb.cmd, tb.arg)} disabled={htmlMode} className={`${btnClass} ${disCls}`}>
                <tb.icon className="w-4 h-4" />
              </button>
            ))}
            <span className="w-px h-5 bg-gray-200 dark:bg-slate-700 mx-1" />
          </div>
        ))}

        {/* Link — popover in-app */}
        <div className="relative" ref={linkPopRef}>
          <button
            type="button"
            title={t('editor.link')}
            onMouseDown={(e) => e.preventDefault()}
            onClick={() => (linkOpen ? setLinkOpen(false) : openLinkPopover())}
            disabled={htmlMode}
            className={`${btnClass} ${disCls} ${linkOpen ? 'bg-gray-200 dark:bg-slate-700' : ''}`}
          >
            <Link2 className="w-4 h-4" />
          </button>

          {linkOpen && (
            <div className="absolute left-0 top-full mt-1.5 z-30 w-72 rounded-xl border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-800 shadow-xl p-3">
              <label className="block text-[11px] font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500 mb-1">{t('editor.linkUrl')}</label>
              <input
                autoFocus
                type="url"
                value={linkUrl}
                onChange={(e) => setLinkUrl(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); applyLink(); } if (e.key === 'Escape') setLinkOpen(false); }}
                placeholder="https://…"
                className="w-full h-9 px-2.5 rounded-lg border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-[13px] text-slate-900 dark:text-slate-100 outline-none focus:ring-2 focus:ring-brand-500/30 focus:border-brand-400"
              />
              {!linkHasSelection && (
                <>
                  <label className="block text-[11px] font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500 mt-2.5 mb-1">{t('editor.linkText')}</label>
                  <input
                    type="text"
                    value={linkText}
                    onChange={(e) => setLinkText(e.target.value)}
                    onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); applyLink(); } if (e.key === 'Escape') setLinkOpen(false); }}
                    placeholder={t('editor.linkTextOptional')}
                    className="w-full h-9 px-2.5 rounded-lg border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-[13px] text-slate-900 dark:text-slate-100 outline-none focus:ring-2 focus:ring-brand-500/30 focus:border-brand-400"
                  />
                </>
              )}
              <div className="flex items-center justify-end gap-2 mt-3">
                <button type="button" onClick={() => setLinkOpen(false)} className="px-3 h-8 rounded-lg text-[13px] font-medium text-slate-500 hover:bg-gray-100 dark:text-slate-400 dark:hover:bg-slate-700 transition-colors">
                  {t('editor.linkCancel')}
                </button>
                <button type="button" onClick={applyLink} className="px-3 h-8 rounded-lg text-[13px] font-semibold bg-brand-600 text-white hover:bg-brand-700 transition-colors inline-flex items-center gap-1.5">
                  <Link2 className="w-3.5 h-3.5" /> {t('editor.linkInsert')}
                </button>
              </div>
            </div>
          )}
        </div>

        <button type="button" title={t('editor.clearFormat')} onMouseDown={(e) => e.preventDefault()} onClick={clearFormat} disabled={htmlMode} className={`${btnClass} ${disCls}`}>
          <Eraser className="w-4 h-4" />
        </button>

        {/* Đính kèm tệp — ngay trong toolbar (như mail) */}
        {attachEnabled && (
          <>
            <span className="w-px h-5 bg-gray-200 dark:bg-slate-700 mx-1" />
            <input ref={fileInputRef} type="file" multiple className="hidden" onChange={(e) => addFiles(e.target.files)} />
            <button type="button" title={t('attach.add')} onClick={() => fileInputRef.current?.click()} className={`${btnClass} relative`}>
              <Paperclip className="w-4 h-4" />
              {files.length > 0 && (
                <span className="absolute -top-0.5 -right-0.5 min-w-[15px] h-[15px] px-1 rounded-full bg-brand-600 text-white text-[10px] font-bold flex items-center justify-center">{files.length}</span>
              )}
            </button>
          </>
        )}

        <div className="flex-1" />

        <button
          type="button"
          title={t('editor.viewHtml')}
          onClick={() => setHtmlMode((m) => !m)}
          className={`flex items-center gap-1.5 px-2.5 h-8 rounded-md text-xs font-medium transition-colors ${htmlMode ? 'bg-brand-600 text-white' : 'text-gray-600 hover:bg-gray-200 dark:text-slate-400 dark:hover:bg-slate-700'}`}
        >
          <Code className="w-3.5 h-3.5" /> HTML
        </button>
      </div>

      {/* Editor / HTML source. */}
      <div className={`relative flex flex-col ${expanded ? 'flex-1 min-h-0' : ''}`}>
        {htmlMode ? (
          <textarea
            value={value}
            onChange={(e) => onChange(e.target.value)}
            placeholder={t('editor.htmlPlaceholder')}
            className={`${expanded ? 'flex-1 resize-none' : 'min-h-[300px] resize-y'} w-full px-3 py-2.5 text-sm font-mono text-gray-800 dark:text-slate-100 dark:bg-slate-800 outline-none leading-relaxed`}
          />
        ) : (
          <div className={`relative ${expanded ? 'flex-1 min-h-0' : ''}`}>
            <div
              ref={editorRef}
              contentEditable
              onInput={(e) => onChange(e.currentTarget.innerHTML)}
              className={`wh-rte ${expanded ? 'h-full overflow-y-auto' : 'min-h-[300px]'} px-3 py-2.5 text-sm text-gray-800 dark:text-slate-100 dark:bg-slate-800 outline-none leading-relaxed`}
            />
            {!value && (
              <div className="absolute top-2.5 left-3 text-sm text-gray-400 dark:text-slate-500 pointer-events-none">
                {placeholder ?? t('sendEmail.contentPlaceholder')}
              </div>
            )}
          </div>
        )}
      </div>

      {/* Chips tệp đính kèm */}
      {attachEnabled && files.length > 0 && (
        <div className="flex flex-wrap gap-2 px-3 py-2.5 border-t border-gray-200 dark:border-slate-700 bg-gray-50/70 dark:bg-slate-800/60 shrink-0">
          {files.map((f, i) => (
            <span key={fileKey(f)} className="inline-flex items-center gap-1.5 max-w-[220px] pl-2 pr-1 py-1 rounded-lg border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-[12px]">
              <FileText className="w-3.5 h-3.5 shrink-0 text-gray-400 dark:text-slate-500" />
              <span className="truncate text-gray-700 dark:text-slate-200">{f.name}</span>
              <span className="shrink-0 text-gray-400 dark:text-slate-500">{formatFileSize(f.size)}</span>
              <button type="button" onClick={() => removeFile(i)} className="shrink-0 p-0.5 rounded text-gray-400 hover:text-red-500 dark:text-slate-500 dark:hover:text-red-400 transition-colors" aria-label={t('attach.remove')}>
                <X className="w-3.5 h-3.5" />
              </button>
            </span>
          ))}
          {overLimit && <p className="basis-full text-[12px] text-red-500 dark:text-red-400 m-0">{t('attach.tooLarge')}</p>}
        </div>
      )}

      {/* Thanh dưới: nút phóng to / thu nhỏ ở góc phải */}
      <div className="flex items-center justify-end px-2 py-1 border-t border-gray-200 dark:border-slate-700 bg-gray-50 dark:bg-slate-800 shrink-0">
        <button
          type="button"
          title={expanded ? t('editor.collapse') : t('editor.expand')}
          onClick={() => setExpanded((e) => !e)}
          className="flex items-center gap-1.5 px-2 h-7 rounded-md text-xs font-medium text-gray-500 hover:bg-gray-200 hover:text-gray-700 dark:text-slate-400 dark:hover:bg-slate-700 dark:hover:text-slate-200 transition-colors"
        >
          {expanded ? <Minimize2 className="w-3.5 h-3.5" /> : <Maximize2 className="w-3.5 h-3.5" />}
          {expanded ? t('editor.collapseShort') : t('editor.expand')}
        </button>
      </div>
    </div>
  );

  if (expanded) {
    return createPortal(
      <div
        className="fixed inset-0 z-[9998] bg-black/50 backdrop-blur-sm flex items-center justify-center p-4 sm:p-8"
        onMouseDown={(e) => { if (e.target === e.currentTarget) setExpanded(false); }}
      >
        <div className="w-full max-w-4xl h-[88vh] flex flex-col">
          {editor}
        </div>
      </div>,
      document.body,
    );
  }

  return editor;
}
