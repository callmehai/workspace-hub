import { useState, useCallback, type ReactNode } from 'react';
import { ChevronDown, Check } from 'lucide-react';
import { useI18n } from '../hooks/useI18n';
import { useFloatingMenu } from '../hooks/useFloatingMenu';

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
  /** Controlled — dùng khi parent cần đóng các overlay khác khi mở cái này. */
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
}

/** Dropdown/listbox tự style (thay native <select>) — khớp tông brand, bo góc, có tick chọn. */
export function Select({
  value,
  onChange,
  options,
  placeholder,
  className = '',
  disabled,
  dropUp,
  icon,
  open: openProp,
  onOpenChange,
}: SelectProps) {
  const { t } = useI18n();
  const ph = placeholder ?? t('common.select');
  const [internalOpen, setInternalOpen] = useState(false);
  const selected = options.find((o) => o.value === value);

  const isControlled = openProp !== undefined;
  const open = isControlled ? openProp : internalOpen;

  const setOpen = useCallback((next: boolean) => {
    if (isControlled) onOpenChange?.(next);
    else setInternalOpen(next);
  }, [isControlled, onOpenChange]);

  const { refs, floatingStyles, getReferenceProps, getFloatingProps, FloatingPortal } = useFloatingMenu({
    open,
    onOpenChange: setOpen,
    placement: dropUp ? 'top-start' : 'bottom-start',
    matchWidth: true,
  });

  return (
    <div className="relative">
      <button
        ref={refs.setReference}
        type="button"
        disabled={disabled}
        {...getReferenceProps({
          onClick: () => {
            if (!disabled) setOpen(!open);
          },
        })}
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

      {open && (
        <FloatingPortal>
          <div
            ref={refs.setFloating}
            style={floatingStyles}
            {...getFloatingProps()}
            className="bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-xl shadow-xl py-1.5 max-h-60 overflow-auto"
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
          </div>
        </FloatingPortal>
      )}
    </div>
  );
}
