import { useCallback, useState } from 'react';
import ReactDatePicker, { registerLocale } from 'react-datepicker';
import 'react-datepicker/dist/react-datepicker.css';
import './DateTimePicker.css';
import { vi } from 'date-fns/locale/vi';
import { enUS } from 'date-fns/locale/en-US';
import { format } from 'date-fns';
import { Calendar as CalendarIcon, X } from 'lucide-react';
import { useI18n } from '../hooks/useI18n';
import { useFloatingMenu } from '../hooks/useFloatingMenu';
import { dateKey, parseDateKey } from '../lib/calendarFormUtils';

registerLocale('vi', vi);
registerLocale('en', enUS);

export interface DatePickerProps {
  /** YYYY-MM-DD */
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

/** 7 cột × (2rem + 4px margin) + padding tháng 20px */
const CALENDAR_POP_W = 7 * 36 + 20;

export function DatePicker({
  value,
  onChange,
  className,
  placeholder,
  variant = 'default',
  open: openProp,
  onOpenChange,
}: DatePickerProps) {
  const isChip = variant === 'chip';
  const { t, lang } = useI18n();
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
    width: CALENDAR_POP_W,
    role: 'dialog',
  });

  const selected = value ? parseDateKey(value) : null;
  const displayText = selected ? format(selected, 'dd/MM/yyyy') : '';

  const toggleOpen = () => {
    if (open) setOpen(false);
    else setOpen(true);
  };

  const handleSelect = (picked: Date | null) => {
    if (!picked) return;
    onChange(dateKey(picked));
    setOpen(false);
  };

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
        {!isChip && <CalendarIcon className="h-4 w-4 shrink-0 text-slate-400" />}
        <span className={`min-w-0 whitespace-nowrap tabular-nums ${isChip ? '' : 'flex-1'} ${selected ? 'text-slate-900 dark:text-slate-100' : 'text-slate-400'}`}>
          {selected ? displayText : (placeholder ?? t('calendar.date'))}
        </span>
        {!isChip && selected && (
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
            className="wh-dtp w-fit overflow-hidden rounded-xl border border-gray-200 bg-white shadow-2xl dark:border-slate-700 dark:bg-slate-800"
          >
            <ReactDatePicker
              inline
              fixedHeight={false}
              selected={selected}
              onChange={handleSelect}
              locale={lang === 'en' ? 'en' : 'vi'}
            />
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
