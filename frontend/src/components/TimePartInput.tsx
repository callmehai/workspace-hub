import type { KeyboardEvent } from 'react';
import { ChevronUp, ChevronDown } from 'lucide-react';

export interface TimePartInputProps {
  value: string;
  placeholder: string;
  ariaLabel: string;
  onChange: (raw: string) => void;
  onBlur: () => void;
  onStep: (delta: number) => void;
}

export function TimePartInput({ value, placeholder, ariaLabel, onChange, onBlur, onStep }: TimePartInputProps) {
  return (
    <div className="flex items-stretch h-9 rounded-lg border border-gray-300 dark:border-slate-700 bg-white dark:bg-slate-800 overflow-hidden transition-colors focus-within:border-brand-500 focus-within:ring-2 focus-within:ring-brand-500/20">
      <input
        type="text"
        inputMode="numeric"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onBlur={onBlur}
        onKeyDown={(e: KeyboardEvent<HTMLInputElement>) => {
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
