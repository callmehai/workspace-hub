import { useState, type KeyboardEvent, type ClipboardEvent } from 'react';
import { X, Plus } from 'lucide-react';
import toast from 'react-hot-toast';

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

interface EmailChipsInputProps {
  value: string[];
  onChange: (value: string[]) => void;
  placeholder?: string;
}

/** Nhập nhiều email dạng chip/tag: gõ + Enter/phẩy/dấu cách hoặc nút "+" để thêm, X để xoá. */
export function EmailChipsInput({ value, onChange, placeholder }: EmailChipsInputProps) {
  const [draft, setDraft] = useState('');

  const addFrom = (raw: string) => {
    const parts = raw.split(/[,;\s]+/).map((s) => s.trim()).filter(Boolean);
    const toAdd: string[] = [];
    for (const p of parts) {
      if (!EMAIL_RE.test(p)) { toast.error(`"${p}" không phải email hợp lệ`); continue; }
      if (value.includes(p) || toAdd.includes(p)) continue; // bỏ trùng
      toAdd.push(p);
    }
    if (toAdd.length) onChange([...value, ...toAdd]);
  };

  const commit = () => {
    if (draft.trim()) { addFrom(draft); setDraft(''); }
  };

  const removeAt = (i: number) => onChange(value.filter((_, idx) => idx !== i));

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (['Enter', ',', ';', ' ', 'Tab'].includes(e.key)) {
      if (draft.trim()) { e.preventDefault(); commit(); }
    } else if (e.key === 'Backspace' && !draft && value.length) {
      removeAt(value.length - 1);
    }
  };

  const onPaste = (e: ClipboardEvent<HTMLInputElement>) => {
    const text = e.clipboardData.getData('text');
    if (/[,;\s]/.test(text)) { e.preventDefault(); addFrom(text); }
  };

  return (
    <div className="w-full min-h-9 flex flex-wrap items-center gap-1.5 px-2 py-1.5 mb-3 border border-gray-300 dark:border-slate-700 rounded-lg bg-white dark:bg-slate-800 transition-colors focus-within:ring-2 focus-within:ring-brand-500/20 focus-within:border-brand-500">
      {value.map((email, i) => (
        <span
          key={email}
          className="inline-flex items-center gap-1 pl-2.5 pr-1 py-0.5 rounded-full bg-brand-50 dark:bg-brand-500/10 text-brand-700 dark:text-brand-400 text-[13px] max-w-full"
        >
          <span className="truncate">{email}</span>
          <button
            type="button"
            onClick={() => removeAt(i)}
            className="shrink-0 w-4 h-4 flex items-center justify-center rounded-full text-brand-500 dark:text-brand-400 hover:bg-brand-100 dark:hover:bg-brand-500/20 hover:text-brand-700 dark:hover:text-brand-300"
            aria-label={`Xoá ${email}`}
          >
            <X className="w-3 h-3" />
          </button>
        </span>
      ))}
      <input
        value={draft}
        onChange={(e) => setDraft(e.target.value)}
        onKeyDown={onKeyDown}
        onPaste={onPaste}
        onBlur={commit}
        placeholder={value.length === 0 ? placeholder : ''}
        className="flex-1 min-w-[200px] h-6 bg-transparent outline-none text-sm text-slate-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500"
      />
      {draft.trim() && (
        <button
          type="button"
          onClick={commit}
          className="shrink-0 w-6 h-6 flex items-center justify-center rounded-md text-brand-600 dark:text-brand-400 hover:bg-brand-50 dark:hover:bg-brand-500/10"
          aria-label="Thêm email"
        >
          <Plus className="w-4 h-4" />
        </button>
      )}
    </div>
  );
}
