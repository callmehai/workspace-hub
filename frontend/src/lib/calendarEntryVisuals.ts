/** Màu chip lịch nội bộ WH — không sync Google colorId. */
export type CalendarEntryKind = 'event' | 'scheduled' | 'jira';

/** Dot accent (legend, popup title) — khớp chip trên lịch. */
export function calendarEntryAccentDot(kind: CalendarEntryKind, allDay: boolean): string {
  if (kind === 'scheduled') return 'bg-blue-500';
  if (kind === 'jira') return 'bg-fuchsia-500';
  return allDay ? 'bg-emerald-500' : 'bg-amber-500';
}

export const LAYER_TOGGLE_ACTIVE: Record<CalendarEntryKind, string> = {
  event: 'border-amber-200 bg-amber-50 text-amber-800 dark:border-amber-500/30 dark:bg-amber-500/15 dark:text-amber-200',
  scheduled: 'border-blue-200 bg-blue-50 text-blue-800 dark:border-blue-500/30 dark:bg-blue-500/15 dark:text-blue-200',
  jira: 'border-fuchsia-200 bg-fuchsia-50 text-fuchsia-800 dark:border-fuchsia-500/30 dark:bg-fuchsia-500/15 dark:text-fuchsia-200',
};

export const LAYER_TOGGLE_INACTIVE =
  'border-slate-200 bg-white text-slate-500 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-400';

export const LAYER_LEGEND_DOTS: Record<CalendarEntryKind, string[]> = {
  event: ['bg-amber-500', 'bg-emerald-500'],
  scheduled: ['bg-blue-500'],
  jira: ['bg-fuchsia-500'],
};
