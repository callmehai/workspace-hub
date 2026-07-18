import { useState, useRef, useEffect, useLayoutEffect, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { ChevronDown, Check } from 'lucide-react';
import { useI18n } from '../hooks/useI18n';

export interface SelectOption {
  value: string;
  label: string;
}

interface SelectProps {
  value: string;
  onChange: (value: string) => void;
  options: SelectOption[];
  placeholder?: string;
  /** Thêm class cho nút trigger (thường set height, vd "h-9"). */
  className?: string;
  disabled?: boolean;
  /** Bung menu LÊN TRÊN (dùng khi select nằm đáy trang, vd footer phân trang). */
  dropUp?: boolean;
  /** Icon nhỏ đứng trước nhãn (vd Briefcase cho chọn dự án). */
  icon?: ReactNode;
}

/** Dropdown/listbox tự style (thay native <select>) — khớp tông brand, bo góc, có tick chọn. */
export function Select({ value, onChange, options, placeholder, className = '', disabled, dropUp, icon }: SelectProps) {
  const { t } = useI18n();
  const ph = placeholder ?? t('common.select');
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const [pos, setPos] = useState<{ top: number; left: number; width: number } | null>(null);
  const selected = options.find((o) => o.value === value);

  useEffect(() => {
    if (!open) return;
    const onDocClick = (e: MouseEvent) => {
      const target = e.target as Node;
      // Menu render qua portal (ngoài `ref`) → phải loại trừ riêng, nếu không bấm chọn sẽ bị coi là click-outside.
      if (ref.current?.contains(target) || menuRef.current?.contains(target)) return;
      setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    document.addEventListener('mousedown', onDocClick);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDocClick);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  // Menu render ra document.body (portal) nên phải tự đo vị trí nút trigger.
  // Lý do dùng portal: trong dialog/list có `overflow-hidden|auto`, menu `absolute` bị CẮT
  // theo hình học — z-index không cứu được.
  useLayoutEffect(() => {
    if (!open) return;
    const place = () => {
      const r = ref.current?.getBoundingClientRect();
      if (!r) return;
      const menuH = menuRef.current?.offsetHeight ?? 240;
      // Không đủ chỗ bên dưới → bung lên trên (hoặc khi caller ép dropUp).
      const flipUp = dropUp || (r.bottom + menuH + 8 > window.innerHeight && r.top - menuH - 8 > 0);
      setPos({
        top: flipUp ? r.top - menuH - 6 : r.bottom + 6,
        left: r.left,
        width: r.width,
      });
    };
    place();
    // Cuộn/resize khi menu đang mở → bám theo nút (portal không tự dính).
    window.addEventListener('scroll', place, true);
    window.addEventListener('resize', place);
    return () => {
      window.removeEventListener('scroll', place, true);
      window.removeEventListener('resize', place);
    };
  }, [open, dropUp, options.length]);

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        disabled={disabled}
        onClick={() => setOpen((o) => !o)}
        className={`w-full flex items-center justify-between gap-2 px-3 border rounded-lg text-sm bg-white dark:bg-slate-800 transition-colors disabled:opacity-60 disabled:cursor-not-allowed ${
          open ? 'border-brand-500 ring-2 ring-brand-500/20' : 'border-gray-300 hover:border-gray-400 dark:border-slate-700 dark:hover:border-slate-600'
        } ${className}`}
      >
        <span className={`min-w-0 flex items-center gap-2 truncate text-left ${selected ? 'text-gray-800 dark:text-slate-100' : 'text-gray-400 dark:text-slate-500'}`}>
          {icon && <span className="shrink-0 text-gray-400 dark:text-slate-500">{icon}</span>}
          <span className="truncate">{selected ? selected.label : ph}</span>
        </span>
        <ChevronDown className={`w-4 h-4 text-gray-400 dark:text-slate-500 shrink-0 transition-transform ${open ? 'rotate-180' : ''}`} />
      </button>

      {open && createPortal(
        <div
          ref={menuRef}
          style={{
            position: 'fixed',
            top: pos?.top ?? -9999,
            left: pos?.left ?? -9999,
            minWidth: pos?.width,
            visibility: pos ? 'visible' : 'hidden',
          }}
          className="z-[200] bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-xl shadow-xl py-1.5 max-h-60 overflow-auto"
        >
          {options.length === 0 ? (
            <div className="px-3 py-2 text-sm text-gray-400 dark:text-slate-500">{t('common.noOptions')}</div>
          ) : (
            options.map((o) => {
              const active = o.value === value;
              return (
                <button
                  key={o.value}
                  type="button"
                  onClick={() => {
                    onChange(o.value);
                    setOpen(false);
                  }}
                  className={`w-full flex items-center justify-between gap-3 px-3 py-2 text-sm text-left transition-colors ${
                    active ? 'bg-brand-50 text-brand-700 font-medium dark:bg-brand-500/15 dark:text-brand-300' : 'text-gray-700 hover:bg-gray-50 dark:text-slate-300 dark:hover:bg-slate-700'
                  }`}
                >
                  <span className="truncate">{o.label}</span>
                  {active && <Check className="w-4 h-4 shrink-0" />}
                </button>
              );
            })
          )}
        </div>,
        document.body,
      )}
    </div>
  );
}
