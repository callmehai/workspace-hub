import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Clock, X } from 'lucide-react';
import './DateTimePicker.css';
import { useI18n } from '../hooks/useI18n';
import { useFloatingMenu } from '../hooks/useFloatingMenu';

export interface TimePickerProps {
  /** HH:mm (24h) */
  value: string;
  onChange: (value: string) => void;
  className?: string;
  placeholder?: string;
  /** `chip` = Google Calendar style: nền xám mềm, không icon/clear. */
  variant?: 'default' | 'chip';
  /** Controlled popover — dùng cùng parent để chỉ mở 1 picker/lúc. */
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
}

const POP_W = 200;

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

  // We repeat the items 5 times to create a looping list
  const repeatedItems = useMemo(() => {
    const result: { value: number; key: string }[] = [];
    for (let c = 0; c < 5; c++) {
      items.forEach((item, idx) => {
        result.push({ value: item, key: `${c}-${idx}-${item}` });
      });
    }
    return result;
  }, [items]);

  useEffect(() => {
    const el = listRef.current;
    if (!el) return;
    const selectedButtons = el.querySelectorAll('[data-selected="true"]');
    const middleBtn = selectedButtons[2] || selectedButtons[0];
    middleBtn?.scrollIntoView({ block: 'center' });
  }, [scrollKey, selected]);

  const handleScroll = (e: React.UIEvent<HTMLUListElement>) => {
    const el = e.currentTarget;
    const singleSetHeight = el.scrollHeight / 5;
    if (el.scrollTop < singleSetHeight) {
      el.scrollTop += singleSetHeight * 2;
    } else if (el.scrollTop > el.scrollHeight - singleSetHeight * 2) {
      el.scrollTop -= singleSetHeight * 2;
    }
  };

  return (
    <div className="wh-time-picker__column">
      <p className="wh-time-picker__caption">{label}</p>
      <ul ref={listRef} onScroll={handleScroll} className="wh-time-picker__list hide-scrollbar">
        {repeatedItems.map(({ value, key }) => (
          <li key={key}>
            <button
              type="button"
              data-selected={value === selected ? 'true' : undefined}
              className={`wh-time-picker__item${value === selected ? ' wh-time-picker__item--selected' : ''}`}
              onClick={() => onSelect(value)}
            >
              {String(value).padStart(2, '0')}
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}

export function TimePicker({
  value,
  onChange,
  className,
  placeholder,
  variant = 'default',
  open: openProp,
  onOpenChange,
}: TimePickerProps) {
  const isChip = variant === 'chip';
  const { t } = useI18n();
  const [internalOpen, setInternalOpen] = useState(false);

  const isControlled = openProp !== undefined;
  const open = isControlled ? openProp : internalOpen;

  const setOpen = useCallback((next: boolean) => {
    if (isControlled) onOpenChange?.(next);
    else setInternalOpen(next);
  }, [isControlled, onOpenChange]);

  const { refs, floatingStyles, getReferenceProps, getFloatingProps, FloatingPortal } = useFloatingMenu({
    open,
    onOpenChange: setOpen,
    matchWidth: false,
    width: POP_W,
    role: 'dialog',
  });

  const { hour, minute } = parseTime(value || '00:00');

  const toggleOpen = () => {
    if (open) setOpen(false);
    else setOpen(true);
  };

  const setHour = (h: number) => onChange(toTimeString(h, minute));
  const setMinute = (m: number) => onChange(toTimeString(hour, m));

  const baseTrigger = isChip
    ? `inline-flex h-9 items-center justify-center rounded-md border-0 bg-[#e8eaed] px-3 text-[13px] font-medium text-slate-800 outline-none transition hover:bg-[#dde1e6] dark:bg-slate-700 dark:text-slate-100 dark:hover:bg-slate-600 ${className ?? ''}`
    : `flex h-9 w-full items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-2.5 text-left text-[13px] outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/15 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100 ${className ?? ''}`;

  return (
    <div className={isChip ? 'relative inline-block' : 'relative'}>
      <button
        ref={refs.setReference}
        type="button"
        {...getReferenceProps({ onClick: toggleOpen })}
        className={`${baseTrigger} ${open ? (isChip ? 'ring-2 ring-brand-500/25' : '!border-brand-500 ring-2 ring-brand-500/20') : ''}`}
      >
        {!isChip && <Clock className="h-4 w-4 shrink-0 text-slate-400" />}
        <span className={`min-w-0 whitespace-nowrap tabular-nums ${isChip ? '' : 'flex-1'} ${value ? 'text-slate-900 dark:text-slate-100' : 'text-slate-400'}`}>
          {value || (placeholder ?? 'HH:mm')}
        </span>
        {!isChip && value && (
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

      {open && (
        <FloatingPortal>
          <div
            ref={refs.setFloating}
            style={floatingStyles}
            {...getFloatingProps()}
            className="wh-time-picker overflow-hidden rounded-xl border border-gray-200 bg-white shadow-2xl dark:border-slate-700 dark:bg-slate-800"
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
          </div>
        </FloatingPortal>
      )}
    </div>
  );
}
