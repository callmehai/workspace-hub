import { useCallback, useState } from 'react';
import DatePicker, { registerLocale } from 'react-datepicker';
import 'react-datepicker/dist/react-datepicker.css';
import './DateTimePicker.css';
import { vi } from 'date-fns/locale/vi';
import { enUS } from 'date-fns/locale/en-US';
import {
  addMinutes, addHours, addDays,
  setHours, setMinutes, setSeconds, setMilliseconds,
  startOfDay, isBefore, format,
} from 'date-fns';
import { Calendar as CalendarIcon, Clock, ChevronUp, ChevronDown, X, Zap } from 'lucide-react';
import { useI18n } from '../hooks/useI18n';
import { useFloatingMenu } from '../hooks/useFloatingMenu';
import type { TranslationKey } from '../i18n/translations';

registerLocale('vi', vi);
registerLocale('en', enUS);

interface DateTimePickerProps {
  value: Date | null;
  onChange: (date: Date | null) => void;
  className?: string;
  placeholder?: string;
}

const at = (d: Date, h: number, m = 0) =>
  setMilliseconds(setSeconds(setMinutes(setHours(d, h), m), 0), 0);

const tidy = (d: Date) =>
  setMilliseconds(setSeconds(setMinutes(d, Math.ceil(d.getMinutes() / 5) * 5), 0), 0);

interface Preset { labelKey: TranslationKey; get: () => Date; }

// Preset hướng tương lai — email hẹn giờ chỉ gửi ở thời điểm sau hiện tại.
const PRESETS: Preset[] = [
  { labelKey: 'dtp.in30min', get: () => addMinutes(new Date(), 30) },
  { labelKey: 'dtp.in1h', get: () => addHours(new Date(), 1) },
  { labelKey: 'dtp.in3h', get: () => addHours(new Date(), 3) },
  { labelKey: 'dtp.tonight8', get: () => at(new Date(), 20) },
  { labelKey: 'dtp.tomorrow9', get: () => at(addDays(new Date(), 1), 9) },
  { labelKey: 'dtp.in3days', get: () => addDays(new Date(), 3) },
  { labelKey: 'dtp.in1w', get: () => addDays(new Date(), 7) },
];

// ─── Spinner input cho giờ / phút ────────────────────────────────────────────
interface TimePartProps {
  value: string;
  placeholder: string;
  ariaLabel: string;
  onChange: (raw: string) => void;
  onBlur: () => void;
  onStep: (delta: number) => void;
}

function TimePart({ value, placeholder, ariaLabel, onChange, onBlur, onStep }: TimePartProps) {
  return (
    <div className="flex items-stretch h-9 rounded-lg border border-gray-300 dark:border-slate-700 bg-white dark:bg-slate-800 overflow-hidden transition-colors focus-within:border-brand-500 focus-within:ring-2 focus-within:ring-brand-500/20">
      <input
        type="text"
        inputMode="numeric"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onBlur={onBlur}
        onKeyDown={(e) => {
          if (e.key === 'ArrowUp') { e.preventDefault(); onStep(1); }
          else if (e.key === 'ArrowDown') { e.preventDefault(); onStep(-1); }
          else if (e.key === 'Enter') { (e.target as HTMLInputElement).blur(); }
        }}
        placeholder={placeholder}
        aria-label={ariaLabel}
        className="w-11 px-2 border-0 bg-transparent text-center text-sm tabular-nums outline-none text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500"
      />
      <div className="flex flex-col border-l border-gray-200 dark:border-slate-700">
        <button type="button" tabIndex={-1} onClick={() => onStep(1)} className="flex-1 px-1 flex items-center justify-center text-gray-500 dark:text-slate-400 hover:bg-gray-100 dark:hover:bg-slate-700">
          <ChevronUp className="w-3 h-3" />
        </button>
        <button type="button" tabIndex={-1} onClick={() => onStep(-1)} className="flex-1 px-1 flex items-center justify-center text-gray-500 dark:text-slate-400 hover:bg-gray-100 dark:hover:bg-slate-700 border-t border-gray-200 dark:border-slate-700">
          <ChevronDown className="w-3 h-3" />
        </button>
      </div>
    </div>
  );
}

// ─── Picker chính ────────────────────────────────────────────────────────────
const POP_W = 440;

export function DateTimePicker({ value, onChange, className, placeholder }: DateTimePickerProps) {
  const { t, lang } = useI18n();
  const [open, setOpen] = useState(false);
  const [hourDraft, setHourDraft] = useState('');
  const [minuteDraft, setMinuteDraft] = useState('');
  const now = new Date();

  const { refs, floatingStyles, getReferenceProps, getFloatingProps, FloatingPortal } = useFloatingMenu({
    open,
    onOpenChange: setOpen,
    matchWidth: false,
    width: POP_W,
    role: 'dialog',
  });

  const syncDrafts = useCallback((d: Date | null) => {
    setHourDraft(d ? format(d, 'HH') : '');
    setMinuteDraft(d ? format(d, 'mm') : '');
  }, []);

  const toggleOpen = () => {
    if (open) {
      setOpen(false);
      return;
    }
    syncDrafts(value);
    setOpen(true);
  };

  const commit = (d: Date | null) => { onChange(d); syncDrafts(d); };

  const handleDateSelect = (picked: Date | null) => {
    if (!picked) { commit(null); return; }
    const base = value ?? new Date();
    const merged = new Date(picked);
    merged.setHours(base.getHours(), base.getMinutes(), 0, 0);
    onChange(merged);
    syncDrafts(merged);
  };

  const applyTime = (hour: number | undefined, minute: number | undefined) => {
    const base = value ?? startOfDay(new Date());
    const merged = new Date(base);
    if (hour !== undefined) merged.setHours(hour);
    if (minute !== undefined) merged.setMinutes(minute);
    merged.setSeconds(0, 0);
    onChange(merged);
  };

  const commitTimePart = (raw: string, max: number, which: 'hour' | 'minute') => {
    const digits = raw.replace(/\D/g, '').slice(0, 2);
    const setDraft = which === 'hour' ? setHourDraft : setMinuteDraft;
    if (digits === '') { setDraft(''); return; }
    const clamped = Math.min(max, Math.max(0, Number.parseInt(digits, 10)));
    setDraft(String(clamped).padStart(2, '0'));
    if (which === 'hour') applyTime(clamped, undefined);
    else applyTime(undefined, clamped);
  };

  const stepPart = (delta: number, which: 'hour' | 'minute') => {
    if (which === 'hour') {
      const next = ((value?.getHours() ?? 0) + delta + 24) % 24;
      setHourDraft(String(next).padStart(2, '0'));
      applyTime(next, undefined);
    } else {
      const next = ((value?.getMinutes() ?? 0) + delta + 60) % 60;
      setMinuteDraft(String(next).padStart(2, '0'));
      applyTime(undefined, next);
    }
  };

  const padOnBlur = (draft: string, max: number, which: 'hour' | 'minute') => {
    const setDraft = which === 'hour' ? setHourDraft : setMinuteDraft;
    if (draft === '') { setDraft(value ? format(value, which === 'hour' ? 'HH' : 'mm') : ''); return; }
    const clamped = Math.min(max, Math.max(0, Number.parseInt(draft, 10)));
    setDraft(String(clamped).padStart(2, '0'));
  };

  const displayText = value ? format(value, lang === 'vi' ? "HH:mm 'ngày' dd/MM/yyyy" : 'HH:mm dd/MM/yyyy') : '';

  return (
    <div className="relative">
      <button
        ref={refs.setReference}
        type="button"
        {...getReferenceProps({ onClick: toggleOpen })}
        className={`flex items-center gap-2 text-left ${className ?? ''} ${open ? '!border-brand-500 ring-2 ring-brand-500/20' : ''}`}
      >
        <CalendarIcon className="w-4 h-4 text-gray-400 dark:text-slate-500 shrink-0" />
        <span className={`flex-1 truncate ${value ? 'text-gray-800 dark:text-slate-200' : 'text-gray-400 dark:text-slate-500'}`}>
          {value ? displayText : (placeholder ?? t('dtp.placeholder'))}
        </span>
        {value && (
          <span
            role="button"
            tabIndex={-1}
            onClick={(e) => { e.stopPropagation(); commit(null); }}
            className="shrink-0 text-gray-400 dark:text-slate-500 hover:text-gray-600 dark:hover:text-slate-300 p-0.5 rounded"
            aria-label={t('dtp.clear')}
          >
            <X className="w-3.5 h-3.5" />
          </span>
        )}
      </button>

      {open && (
        <FloatingPortal>
          <div
            ref={refs.setFloating}
            style={floatingStyles}
            {...getFloatingProps()}
            className="wh-dtp"
          >
            <div className="flex items-stretch rounded-xl overflow-hidden shadow-2xl border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-800">
              {/* Preset column */}
              <div className="w-44 shrink-0 border-r border-gray-100 dark:border-slate-700 bg-gray-50/60 dark:bg-slate-900/40 py-3 px-2.5 flex flex-col">
                <p className="flex items-center gap-1.5 px-2 mb-2 text-[11px] font-semibold uppercase tracking-wider text-gray-400 dark:text-slate-500">
                  <Zap className="w-3.5 h-3.5" /> {t('dtp.quickPick')}
                </p>
                <div className="flex flex-col gap-1">
                  {PRESETS.filter((p) => !isBefore(p.get(), now)).map((p) => {
                    const active = value != null && Math.abs(tidy(p.get()).getTime() - value.getTime()) < 60_000;
                    return (
                      <button
                        key={p.labelKey}
                        type="button"
                        onClick={() => { commit(tidy(p.get())); setOpen(false); }}
                        className={`text-left whitespace-nowrap px-3 py-2 rounded-lg text-[13px] font-medium transition-colors ${
                          active ? 'bg-brand-600 text-white' : 'text-gray-600 dark:text-slate-300 hover:bg-brand-50 dark:hover:bg-brand-500/10 hover:text-brand-700 dark:hover:text-brand-400'
                        }`}
                      >
                        {t(p.labelKey)}
                      </button>
                    );
                  })}
                </div>
              </div>

              {/* Calendar + time + footer */}
              <div className="flex flex-col">
                <DatePicker
                  inline
                  selected={value}
                  onChange={handleDateSelect}
                  minDate={startOfDay(now)}
                  locale={lang === 'en' ? 'en' : 'vi'}
                />
                <div className="flex items-center justify-center gap-2 px-3 py-2.5 border-t border-gray-100 dark:border-slate-700">
                  <Clock className="w-4 h-4 text-gray-400 dark:text-slate-500" />
                  <TimePart
                    value={hourDraft}
                    placeholder="HH"
                    ariaLabel={t('dtp.hour')}
                    onChange={(r) => commitTimePart(r, 23, 'hour')}
                    onBlur={() => padOnBlur(hourDraft, 23, 'hour')}
                    onStep={(d) => stepPart(d, 'hour')}
                  />
                  <span className="font-semibold text-gray-400 dark:text-slate-500">:</span>
                  <TimePart
                    value={minuteDraft}
                    placeholder="mm"
                    ariaLabel={t('dtp.minute')}
                    onChange={(r) => commitTimePart(r, 59, 'minute')}
                    onBlur={() => padOnBlur(minuteDraft, 59, 'minute')}
                    onStep={(d) => stepPart(d, 'minute')}
                  />
                </div>
                <div className="flex items-center justify-between px-3 py-2.5 border-t border-gray-100 dark:border-slate-700">
                  <button
                    type="button"
                    onClick={() => commit(null)}
                    disabled={!value}
                    className="px-3 py-1.5 text-sm font-medium text-gray-500 dark:text-slate-400 rounded-lg hover:bg-gray-100 dark:hover:bg-slate-700 transition-colors disabled:opacity-40 disabled:pointer-events-none"
                  >
                    {t('dtp.clearBtn')}
                  </button>
                  <button
                    type="button"
                    onClick={() => setOpen(false)}
                    className="px-4 py-1.5 text-sm font-semibold text-white bg-brand-600 hover:bg-brand-700 rounded-lg transition-colors"
                  >
                    {t('dtp.done')}
                  </button>
                </div>
              </div>
            </div>
          </div>
        </FloatingPortal>
      )}
    </div>
  );
}
