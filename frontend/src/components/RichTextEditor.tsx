import { useRef, useState, useEffect } from 'react';
import { createPortal } from 'react-dom';
import {
  Bold, Italic, Underline, Strikethrough, List, ListOrdered,
  Link2, Heading2, AlignLeft, AlignCenter, Eraser, Code,
  Maximize2, Minimize2,
} from 'lucide-react';
import './RichTextEditor.css';
import { useI18n } from '../hooks/useI18n';
import type { TranslationKey } from '../i18n/translations';

interface RichTextEditorProps {
  value: string;
  onChange: (html: string) => void;
  placeholder?: string;
  className?: string;
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

/** Trình soạn thảo rich text (WYSIWYG) → HTML, kèm toggle HTML thô + phóng to toàn màn hình. */
export function RichTextEditor({ value, onChange, placeholder, className }: RichTextEditorProps) {
  const { t } = useI18n();
  const editorRef = useRef<HTMLDivElement>(null);
  const [htmlMode, setHtmlMode] = useState(false);
  const [expanded, setExpanded] = useState(false);

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

  const exec = (cmd: string, arg?: string) => {
    editorRef.current?.focus();
    document.execCommand(cmd, false, arg);
    onChange(editorRef.current?.innerHTML ?? '');
  };

  const addLink = () => {
    const url = window.prompt(t('editor.linkPrompt'), 'https://');
    if (url) exec('createLink', url);
  };

  const clearFormat = () => {
    exec('removeFormat');
    exec('formatBlock', 'DIV');
  };

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
        <button type="button" title={t('editor.link')} onMouseDown={(e) => e.preventDefault()} onClick={addLink} disabled={htmlMode} className={`${btnClass} ${disCls}`}>
          <Link2 className="w-4 h-4" />
        </button>
        <button type="button" title={t('editor.clearFormat')} onMouseDown={(e) => e.preventDefault()} onClick={clearFormat} disabled={htmlMode} className={`${btnClass} ${disCls}`}>
          <Eraser className="w-4 h-4" />
        </button>

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

      {/* Editor / HTML source.
          - Thường: cao theo nội dung (min 300px), chạm 52vh thì cuộn bên trong.
          - Phóng to: lấp đầy modal. */}
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

      {/* Thanh dưới: nút phóng to / thu nhỏ ở góc phải (không đè nội dung/scrollbar) */}
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
