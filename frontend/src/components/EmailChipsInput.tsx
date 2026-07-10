import { useState, useRef, useEffect, useCallback, type KeyboardEvent, type ClipboardEvent } from 'react';
import { useQuery } from '@tanstack/react-query';
import { X, Plus } from 'lucide-react';
import toast from 'react-hot-toast';
import { useI18n } from '../hooks/useI18n';
import { sendEmailApi, type ContactSuggestion } from '../lib/sendEmailApi';
import { friendsApi } from '../lib/friendsApi';

/** Gợi ý = bạn bè trong app (ưu tiên, Bạn thân trước) + cache contact Google. */
type Suggestion = ContactSuggestion & { tier?: 'Friend' | 'CloseFriend' };

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const DEBOUNCE_MS = 300;

/** Chuẩn hoá email để so khớp/lưu — khớp backend (lowercase). */
const normalizeEmail = (raw: string) => raw.trim().toLowerCase();

const hasEmail = (list: string[], email: string) =>
  list.some((v) => v.toLowerCase() === email.toLowerCase());

interface EmailChipsInputProps {
  value: string[];
  onChange: (value: string[]) => void;
  placeholder?: string;
  /** Bật gợi ý contact từ cache Google (SCRUM-69). */
  connectionId?: string;
}

/** Nhập nhiều email dạng chip/tag + gợi ý contact khi có connectionId. */
export function EmailChipsInput({ value, onChange, placeholder, connectionId }: EmailChipsInputProps) {
  const { t } = useI18n();
  const [draft, setDraft] = useState('');
  const [debouncedQ, setDebouncedQ] = useState('');
  const [open, setOpen] = useState(false);
  const [activeIdx, setActiveIdx] = useState(0);
  const [labels, setLabels] = useState<Record<string, string>>({});
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const wrapRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => setDebouncedQ(draft.trim()), DEBOUNCE_MS);
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current); };
  }, [draft]);

  const suggestEnabled = !!connectionId && debouncedQ.length >= 2;

  const { data: suggestions = [] } = useQuery({
    queryKey: ['contact-suggest', connectionId, debouncedQ],
    queryFn: () => sendEmailApi.suggestContacts(connectionId!, debouncedQ),
    enabled: suggestEnabled,
    staleTime: 60_000,
    retry: false,
  });

  // Bạn bè trong app — không cần connectionId, khớp theo tên HOẶC email, Bạn thân lên đầu.
  const { data: friendsOverview } = useQuery({
    queryKey: ['friends'],
    queryFn: friendsApi.getOverview,
    staleTime: 60_000,
    retry: false,
  });
  const q = debouncedQ.toLowerCase();
  const friendMatches: Suggestion[] = q.length >= 2
    ? (friendsOverview?.friends ?? [])
        .filter((f) => f.email.toLowerCase().includes(q) || f.fullName.toLowerCase().includes(q))
        .sort((a, b) => Number(b.myTier === 'CloseFriend') - Number(a.myTier === 'CloseFriend'))
        .map((f) => ({ email: f.email, displayName: f.fullName, tier: f.myTier }))
    : [];
  const friendEmails = new Set(friendMatches.map((f) => f.email.toLowerCase()));

  const filtered: Suggestion[] = [
    ...friendMatches,
    ...suggestions.filter((s) => !friendEmails.has(s.email.toLowerCase())),
  ].filter((s) => !value.some((v) => v.toLowerCase() === s.email.toLowerCase()));

  /** Index đang highlight trong dropdown — luôn nằm trong [0, filtered.length). */
  const selectedIndex = filtered.length === 0 ? 0 : Math.min(activeIdx, filtered.length - 1);

  useEffect(() => {
    const onDocClick = (e: MouseEvent) => {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onDocClick);
    return () => document.removeEventListener('mousedown', onDocClick);
  }, []);

  const addEmail = useCallback((email: string, displayName?: string | null) => {
    const trimmed = email.trim();
    if (!EMAIL_RE.test(trimmed)) {
      toast.error(t('chips.invalidEmail', { email: trimmed }));
      return;
    }
    const normalized = normalizeEmail(trimmed);
    if (hasEmail(value, normalized)) return;
    if (displayName) setLabels((prev) => ({ ...prev, [normalized]: displayName }));
    onChange([...value, normalized]);
    setDraft('');
    setOpen(false);
  }, [onChange, t, value]);

  const addFrom = (raw: string) => {
    const parts = raw.split(/[,;\s]+/).map((s) => s.trim()).filter(Boolean);
    const toAdd: string[] = [];
    for (const p of parts) {
      if (!EMAIL_RE.test(p)) { toast.error(t('chips.invalidEmail', { email: p })); continue; }
      const normalized = normalizeEmail(p);
      if (hasEmail(value, normalized) || hasEmail(toAdd, normalized)) continue;
      toAdd.push(normalized);
    }
    if (toAdd.length) onChange([...value, ...toAdd]);
  };

  const commit = () => {
    if (draft.trim()) { addFrom(draft); setDraft(''); setOpen(false); }
  };

  const removeAt = (i: number) => {
    const removed = value[i];
    onChange(value.filter((_, idx) => idx !== i));
    if (removed) {
      setLabels((prev) => {
        const next = { ...prev };
        delete next[removed];
        delete next[normalizeEmail(removed)];
        return next;
      });
    }
  };

  const pickSuggestion = (s: ContactSuggestion) => addEmail(s.email, s.displayName);

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (open && filtered.length > 0) {
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        setActiveIdx((i) => Math.min(i + 1, filtered.length - 1));
        return;
      }
      if (e.key === 'ArrowUp') {
        e.preventDefault();
        setActiveIdx((i) => Math.max(i - 1, 0));
        return;
      }
      if (e.key === 'Escape') {
        setOpen(false);
        return;
      }
      // Enter HOẶC Tab đều chọn gợi ý đang highlight (giống Gmail). preventDefault để Tab không nhảy field.
      if ((e.key === 'Enter' || e.key === 'Tab') && draft.trim()) {
        e.preventDefault();
        pickSuggestion(filtered[selectedIndex]);
        return;
      }
    }

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

  const showDropdown = open && filtered.length > 0;

  return (
    <div ref={wrapRef} className="relative mb-3 min-w-0">
      <div className="w-full min-h-9 flex flex-wrap items-center gap-1.5 px-2 py-1.5 border border-gray-300 dark:border-slate-700 rounded-lg bg-white dark:bg-slate-800 transition-colors focus-within:ring-2 focus-within:ring-brand-500/20 focus-within:border-brand-500">
        {value.map((email, i) => (
          <span
            key={email}
            title={email}
            className="inline-flex items-center gap-1 pl-2.5 pr-1 py-0.5 rounded-full bg-brand-50 dark:bg-brand-500/10 text-brand-700 dark:text-brand-400 text-[13px] max-w-full"
          >
            <span className="truncate">{labels[email] ?? email}</span>
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
          onChange={(e) => { setDraft(e.target.value); setOpen(true); setActiveIdx(0); }}
          onFocus={() => setOpen(true)}
          onKeyDown={onKeyDown}
          onPaste={onPaste}
          onBlur={() => { /* commit on blur only if no dropdown pick */ setTimeout(() => { if (draft.trim()) commit(); }, 150); }}
          placeholder={value.length === 0 ? placeholder : ''}
          className="flex-1 min-w-[6rem] h-6 bg-transparent outline-none text-sm text-slate-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500"
          autoComplete="off"
          role="combobox"
          aria-expanded={showDropdown}
          aria-autocomplete="list"
        />
        {draft.trim() && (
          <button
            type="button"
            onClick={commit}
            className="shrink-0 w-6 h-6 flex items-center justify-center rounded-md text-brand-600 dark:text-brand-400 hover:bg-brand-50 dark:hover:bg-brand-500/10"
            aria-label={t('chips.addEmail')}
          >
            <Plus className="w-4 h-4" />
          </button>
        )}
      </div>

      {showDropdown && (
        <ul
          className="absolute z-50 left-0 right-0 mt-1 max-h-48 overflow-y-auto rounded-lg border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-800 shadow-lg py-1"
          role="listbox"
        >
          {filtered.map((s, i) => (
            <li key={s.email} role="option" aria-selected={i === selectedIndex}>
              <button
                type="button"
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => pickSuggestion(s)}
                className={`w-full text-left px-3 py-2 text-sm flex flex-col gap-0.5 ${
                  i === selectedIndex
                    ? 'bg-brand-50 dark:bg-brand-500/10 text-brand-800 dark:text-brand-300'
                    : 'text-slate-800 dark:text-slate-200 hover:bg-gray-50 dark:hover:bg-slate-700/60'
                }`}
              >
                <span className="flex items-center gap-1.5 min-w-0">
                  <span className="font-medium truncate">{s.displayName ?? s.email}</span>
                  {s.tier === 'CloseFriend' && (
                    <span className="shrink-0 rounded-full bg-amber-50 px-1.5 py-px text-[11px] font-semibold text-amber-700 dark:bg-amber-500/15 dark:text-amber-300">
                      ⭐ {t('friends.closeFriend')}
                    </span>
                  )}
                  {s.tier === 'Friend' && (
                    <span className="shrink-0 rounded-full bg-brand-50 px-1.5 py-px text-[11px] font-semibold text-brand-700 dark:bg-brand-500/15 dark:text-brand-300">
                      {t('friends.list')}
                    </span>
                  )}
                </span>
                {s.displayName && (
                  <span className="text-xs text-gray-500 dark:text-slate-400 truncate">{s.email}</span>
                )}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
