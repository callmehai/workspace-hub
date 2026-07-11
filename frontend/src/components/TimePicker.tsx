import { useCallback, useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Clock, X } from 'lucide-react';
import './DateTimePicker.css';
import { useI18n } from '../hooks/useI18n';

export interface TimePickerProps {
  /** HH:mm (24h) */
  value: string;
  onChange: (value: string) => void;
  className?: string;
  placeholder?: string;
  /** Controlled popover — dùng cùng parent để chỉ mở 1 picker/lúc. */
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
}

const POP_W = 200;
const POP_H = 280;

const HOURS = Array.from({ length: 24 }, (_, i) => i);
const MINUTES = Array.from({ length: 60 }, (_, i) => i);

function parseTime(value: string): { hour: number; minute: number } {
  const [h, m] = (value || '00:00').split(':').map(Number);
  return {
    hour: Number.isFinite(h) ? h : 0,
    minute: Number.isFinite(m) ? m : 0,
  };
}

function toTimeString(hour: number, minute: number) {
  return `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`;
}

interface ScrollColumnProps {
  label: string;
  items: number[];
  selected: number;
  onSelect: (value: number) => void;
  scrollKey: string;
}

function ScrollColumn({ label, items, selected, onSelect, scrollKey }: ScrollColumnProps) {
  const listRef = useRef<HTMLUListElement>(null);

  useEffect(() => {
    const el = listRef.current?.querySelector('[data-selected="true"]');
    el?.scrollIntoView({ block: 'center' });
  }, [scrollKey, selected]);

  return (
    <div className="wh-time-picker__column">
      <p className="wh-time-picker__caption">{label}</p>
      <ul ref={listRef} className="wh-time-picker__list hide-scrollbar">
        {items.map(item => (
          <li key={item}>
            <button
              type="button"
              data-selected={item === selected ? 'true' : undefined}
              className={`wh-time-picker__item${item === selected ? ' wh-time-picker__item--selected' : ''}`}
              onClick={() => onSelect(item)}
            >
              {String(item).padStart(2, '0')}
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}

export function TimePicker({ value, onChange, className, placeholder, open: openProp, onOpenChange }: TimePickerProps) {
  const { t } = useI18n();
  const [internalOpen, setInternalOpen] = useState(false);
  const [pos, setPos] = useState<{ left: number; top?: number; bottom?: number } | null>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const popRef = useRef<HTMLDivElement>(null);

  const isControlled = openProp !== undefined;
  const open = isControlled ? openProp : internalOpen;

  const setOpen = useCallback((next: boolean) => {
    if (isControlled) onOpenChange?.(next);
    else setInternalOpen(next);
  }, [isControlled, onOpenChange]);

  const { hour, minute } = parseTime(value || '00:00');

  const computePos = useCallback(() => {
    const el = triggerRef.current;
    if (!el) return;
    const r = el.getBoundingClientRect();
    const spaceBelow = window.innerHeight - r.bottom;
    const openUp = spaceBelow < POP_H && r.top > spaceBelow;
    const left = Math.max(8, Math.min(r.left, window.innerWidth - POP_W - 8));
    setPos(openUp
      ? { left, bottom: window.innerHeight - r.top + 6 }
      : { left, top: r.bottom + 6 });
  }, []);

  useEffect(() => {
    if (!open) return;
    const onDocClick = (e: MouseEvent) => {
      const target = e.target as Node;
      if (triggerRef.current?.contains(target) || popRef.current?.contains(target)) return;
      setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false); };
    const onReflow = () => computePos();
    document.addEventListener('mousedown', onDocClick);
    document.addEventListener('keydown', onKey);
    window.addEventListener('resize', onReflow);
    window.addEventListener('scroll', onReflow, true);
    return () => {
      document.removeEventListener('mousedown', onDocClick);
      document.removeEventListener('keydown', onKey);
      window.removeEventListener('resize', onReflow);
      window.removeEventListener('scroll', onReflow, true);
    };
  }, [open, setOpen, computePos]);

  useEffect(() => {
    if (open) computePos();
  }, [open, computePos]);

  const toggleOpen = () => {
    if (open) { setOpen(false); return; }
    setOpen(true);
  };

  const setHour = (h: number) => onChange(toTimeString(h, minute));
  const setMinute = (m: number) => onChange(toTimeString(hour, m));

  const baseTrigger = `flex h-9 w-full items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-2.5 text-left text-[13px] outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/15 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100 ${className ?? ''}`;

  return (
    <div className="relative">
      <button
        ref={triggerRef}
        type="button"
        onClick={toggleOpen}
        className={`${baseTrigger} ${open ? '!border-brand-500 ring-2 ring-brand-500/20' : ''}`}
      >
        <Clock className="h-4 w-4 shrink-0 text-slate-400" />
        <span className={`min-w-0 flex-1 whitespace-nowrap tabular-nums ${value ? 'text-slate-900 dark:text-slate-100' : 'text-slate-400'}`}>
          {value || (placeholder ?? 'HH:mm')}
        </span>
        {value && (
          <span
            role="button"
            tabIndex={-1}
            onClick={(e) => { e.stopPropagation(); onChange(''); }}
            className="shrink-0 rounded text-slate-400 hover:text-slate-600 dark:hover:text-slate-300"
            aria-label={t('dtp.clear')}
          >
            <X className="h-3.5 w-3.5" />
          </span>
        )}
      </button>

      {open && pos && createPortal(
        <div
          ref={popRef}
          style={{ position: 'fixed', left: pos.left, top: pos.top, bottom: pos.bottom, width: POP_W }}
          className="z-[9999] wh-time-picker overflow-hidden rounded-xl border border-gray-200 bg-white shadow-2xl dark:border-slate-700 dark:bg-slate-800"
        >
          <div className="wh-time-picker__columns">
            <ScrollColumn
              label={t('dtp.hour')}
              items={HOURS}
              selected={hour}
              onSelect={setHour}
              scrollKey={`${open}-h-${hour}`}
            />
            <ScrollColumn
              label={t('dtp.minute')}
              items={MINUTES}
              selected={minute}
              onSelect={setMinute}
              scrollKey={`${open}-m-${minute}`}
            />
          </div>
          <div className="flex justify-end border-t border-gray-100 px-3 py-2 dark:border-slate-700">
            <button
              type="button"
              onClick={() => setOpen(false)}
              className="rounded-lg bg-brand-600 px-4 py-1.5 text-sm font-semibold text-white hover:bg-brand-700"
            >
              {t('dtp.done')}
            </button>
          </div>
        </div>,
        document.body,
      )}
    </div>
  );
}
