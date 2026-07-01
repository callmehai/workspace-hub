import { useState, useRef, useEffect } from 'react';
import { createPortal } from 'react-dom';
import DatePicker, { registerLocale } from 'react-datepicker';
import 'react-datepicker/dist/react-datepicker.css';
import './DateTimePicker.css';
import { vi } from 'date-fns/locale/vi';
import {
  addMinutes, addHours, addDays,
  setHours, setMinutes, setSeconds, setMilliseconds,
  startOfDay, isBefore, format,
} from 'date-fns';
import { Calendar as CalendarIcon, Clock, ChevronUp, ChevronDown, X, Zap } from 'lucide-react';

registerLocale('vi', vi);

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

interface Preset { label: string; get: () => Date; }

// Preset hướng tương lai — email hẹn giờ chỉ gửi ở thời điểm sau hiện tại.
const PRESETS: Preset[] = [
  { label: 'Sau 30 phút', get: () => addMinutes(new Date(), 30) },
  { label: 'Sau 1 giờ', get: () => addHours(new Date(), 1) },
  { label: 'Sau 3 giờ', get: () => addHours(new Date(), 3) },
  { label: 'Tối nay 20:00', get: () => at(new Date(), 20) },
  { label: 'Sáng mai 09:00', get: () => at(addDays(new Date(), 1), 9) },
  { label: 'Sau 3 ngày', get: () => addDays(new Date(), 3) },
  { label: 'Sau 1 tuần', get: () => addDays(new Date(), 7) },
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
    <div className="flex items-stretch h-9 rounded-lg border border-gray-300 bg-white overflow-hidden transition-colors focus-within:border-brand-500 focus-within:ring-2 focus-within:ring-brand-500/20">
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
        className="w-11 px-2 border-0 bg-transparent text-center text-sm tabular-nums outline-none"
      />
      <div className="flex flex-col border-l border-gray-200">
        <button type="button" tabIndex={-1} onClick={() => onStep(1)} className="flex-1 px-1 flex items-center justify-center text-gray-500 hover:bg-gray-100">
          <ChevronUp className="w-3 h-3" />
        </button>
        <button type="button" tabIndex={-1} onClick={() => onStep(-1)} className="flex-1 px-1 flex items-center justify-center text-gray-500 hover:bg-gray-100 border-t border-gray-200">
          <ChevronDown className="w-3 h-3" />
        </button>
      </div>
    </div>
  );
}

// ─── Picker chính ────────────────────────────────────────────────────────────
const POP_W = 440;
const POP_H = 420;

export function DateTimePicker({ value, onChange, className, placeholder }: DateTimePickerProps) {
  const [open, setOpen] = useState(false);
  const [hourDraft, setHourDraft] = useState('');
  const [minuteDraft, setMinuteDraft] = useState('');
  const [pos, setPos] = useState<{ left: number; top?: number; bottom?: number } | null>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const popRef = useRef<HTMLDivElement>(null);
  const now = new Date();

  // Định vị popover (fixed) — tự lật LÊN TRÊN khi dưới không đủ chỗ, clamp trong viewport.
  const computePos = () => {
    const el = triggerRef.current;
    if (!el) return;
    const r = el.getBoundingClientRect();
    const spaceBelow = window.innerHeight - r.bottom;
    const openUp = spaceBelow < POP_H && r.top > spaceBelow;
    const left = Math.max(8, Math.min(r.left, window.innerWidth - POP_W - 8));
    setPos(openUp
      ? { left, bottom: window.innerHeight - r.top + 6 }
      : { left, top: r.bottom + 6 });
  };

  useEffect(() => {
    if (!open) return;
    const onDocClick = (e: MouseEvent) => {
      const t = e.target as Node;
      if (triggerRef.current?.contains(t) || popRef.current?.contains(t)) return;
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
  }, [open]);

  const syncDrafts = (d: Date | null) => {
    setHourDraft(d ? format(d, 'HH') : '');
    setMinuteDraft(d ? format(d, 'mm') : '');
  };

  const toggleOpen = () => {
    if (open) { setOpen(false); return; }
    computePos();
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

  const displayText = value ? format(value, "HH:mm 'ngày' dd/MM/yyyy") : '';

  return (
    <div className="relative">
      {/* Trigger */}
      <button
        ref={triggerRef}
        type="button"
        onClick={toggleOpen}
        className={`flex items-center gap-2 text-left ${className ?? ''} ${open ? '!border-brand-500 ring-2 ring-brand-500/20' : ''}`}
      >
        <CalendarIcon className="w-4 h-4 text-gray-400 shrink-0" />
        <span className={`flex-1 truncate ${value ? 'text-gray-800' : 'text-gray-400'}`}>
          {value ? displayText : (placeholder ?? 'Chọn thời gian')}
        </span>
        {value && (
          <span
            role="button"
            tabIndex={-1}
            onClick={(e) => { e.stopPropagation(); commit(null); }}
            className="shrink-0 text-gray-400 hover:text-gray-600 p-0.5 rounded"
            aria-label="Xoá thời gian"
          >
            <X className="w-3.5 h-3.5" />
          </span>
        )}
      </button>

      {/* Popover (portal — thoát overflow-hidden, tự lật lên/xuống) */}
      {open && pos && createPortal(
        <div
          ref={popRef}
          style={{ position: 'fixed', left: pos.left, top: pos.top, bottom: pos.bottom }}
          className="z-[9999] wh-dtp"
        >
          <div className="flex items-stretch rounded-xl overflow-hidden shadow-2xl border border-gray-200 bg-white">
            {/* Preset column */}
            <div className="w-44 shrink-0 border-r border-gray-100 bg-gray-50/60 py-3 px-2.5 flex flex-col">
              <p className="flex items-center gap-1.5 px-2 mb-2 text-[11px] font-semibold uppercase tracking-wider text-gray-400">
                <Zap className="w-3.5 h-3.5" /> Chọn nhanh
              </p>
              <div className="flex flex-col gap-1">
                {PRESETS.filter((p) => !isBefore(p.get(), now)).map((p) => {
                  const active = value != null && Math.abs(tidy(p.get()).getTime() - value.getTime()) < 60_000;
                  return (
                    <button
                      key={p.label}
                      type="button"
                      onClick={() => { commit(tidy(p.get())); setOpen(false); }}
                      className={`text-left whitespace-nowrap px-3 py-2 rounded-lg text-[13px] font-medium transition-colors ${
                        active ? 'bg-brand-600 text-white' : 'text-gray-600 hover:bg-brand-50 hover:text-brand-700'
                      }`}
                    >
                      {p.label}
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
                locale="vi"
              />
              <div className="flex items-center justify-center gap-2 px-3 py-2.5 border-t border-gray-100">
                <Clock className="w-4 h-4 text-gray-400" />
                <TimePart
                  value={hourDraft}
                  placeholder="HH"
                  ariaLabel="Giờ"
                  onChange={(r) => commitTimePart(r, 23, 'hour')}
                  onBlur={() => padOnBlur(hourDraft, 23, 'hour')}
                  onStep={(d) => stepPart(d, 'hour')}
                />
                <span className="font-semibold text-gray-400">:</span>
                <TimePart
                  value={minuteDraft}
                  placeholder="mm"
                  ariaLabel="Phút"
                  onChange={(r) => commitTimePart(r, 59, 'minute')}
                  onBlur={() => padOnBlur(minuteDraft, 59, 'minute')}
                  onStep={(d) => stepPart(d, 'minute')}
                />
              </div>
              <div className="flex items-center justify-between px-3 py-2.5 border-t border-gray-100">
                <button
                  type="button"
                  onClick={() => commit(null)}
                  disabled={!value}
                  className="px-3 py-1.5 text-sm font-medium text-gray-500 rounded-lg hover:bg-gray-100 transition-colors disabled:opacity-40 disabled:pointer-events-none"
                >
                  Xoá
                </button>
                <button
                  type="button"
                  onClick={() => setOpen(false)}
                  className="px-4 py-1.5 text-sm font-semibold text-white bg-brand-600 hover:bg-brand-700 rounded-lg transition-colors"
                >
                  Xong
                </button>
              </div>
            </div>
          </div>
        </div>,
        document.body
      )}
    </div>
  );
}
