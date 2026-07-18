import { CalendarDays, LayoutGrid, List } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { useI18n } from '../../hooks/useI18n';

export type WorkspaceView = 'list' | 'board' | 'calendar';

interface WorkspaceViewSwitcherProps {
  view: WorkspaceView;
  folderId?: string | null;
  sourceType?: string | null;
}

/**
 * Chuyển cách nhìn của workspace và giữ context.
 * Lịch chỉ có ý nghĩa ở Tất cả mục, Google Calendar và folder; Email/Jira/Drive chỉ dùng List/Board.
 */
export function WorkspaceViewSwitcher({ view, folderId, sourceType }: WorkspaceViewSwitcherProps) {
  const navigate = useNavigate();
  const { t } = useI18n();

  const query = (() => {
    const params = new URLSearchParams();
    if (folderId) params.set('folder', folderId);
    if (sourceType) params.set('type', sourceType);
    const value = params.toString();
    return value ? `?${value}` : '';
  })();

  const canOpenCalendar = Boolean(folderId) || !sourceType || sourceType === 'Event';
  const options = [
    { value: 'list' as const, path: '/', label: t('toolbar.list'), Icon: List },
    { value: 'board' as const, path: '/kanban', label: t('toolbar.board'), Icon: LayoutGrid },
    ...(canOpenCalendar
      ? [{ value: 'calendar' as const, path: '/calendar', label: t('toolbar.calendar'), Icon: CalendarDays }]
      : []),
  ];

  return (
    <div
      className={`grid gap-1 p-[3px] shrink-0 bg-white border border-slate-200 rounded-[9px] dark:bg-slate-800 dark:border-slate-700 ${canOpenCalendar ? 'grid-cols-3 w-[336px]' : 'grid-cols-2 w-[228px]'}`}
    >
      {options.map(({ value, path, label, Icon }) => (
        <button
          key={value}
          type="button"
          onClick={() => view !== value && navigate(`${path}${query}`)}
          className={`flex w-full min-w-0 items-center justify-center gap-1.5 px-2.5 py-1.5 rounded-[7px] text-[13px] transition-colors ${view === value
            ? 'bg-brand-50 text-brand-700 font-semibold dark:bg-brand-500/15 dark:text-brand-300'
            : 'text-slate-500 font-medium hover:bg-slate-50 dark:text-slate-400 dark:hover:bg-slate-700'
          }`}
        >
          <Icon className="w-4 h-4 shrink-0" />
          <span className="whitespace-nowrap">{label}</span>
        </button>
      ))}
    </div>
  );
}
