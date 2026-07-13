import { useMemo, useState, type DragEvent, type MouseEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Navigate, useNavigate, useSearchParams } from 'react-router-dom';
import {
  AlertCircle, CalendarDays, ChevronLeft, ChevronRight, Clock3, ExternalLink,
  Flag, Loader2, Mail, MapPin, Plus, Users, X,
} from 'lucide-react';
import toast from 'react-hot-toast';
import { foldersApi, itemsApi } from '../lib/itemsApi';
import { scheduledEmailsApi, type ScheduledEmailDto } from '../lib/scheduledEmailsApi';
import { connectionsApi } from '../lib/connectionsApi';
import type { ItemResponse, PagedResult, PatchItemRequest } from '../types/items';
import { useI18n } from '../hooks/useI18n';
import { usePollingInterval } from '../hooks/usePollingInterval';
import { handleApiError } from '../lib/errorUtils';
import { WorkspaceToolbar } from '../components/workspace/WorkspaceToolbar';
import { parseSourceType } from '../lib/itemVisuals';
import {
  CalendarEventEditorModal,
  type CalendarEventFormValue,
} from '../components/calendar/CalendarEventEditorModal';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { ItemDetail } from '../components/ItemDetail';
import { EventDetailPopup } from '../components/calendar/EventDetailPopup';
import {
  addDays,
  addMonths,
  calendarFormToPatch,
  calendarQueryRange,
  calendarRangeToApiTimes,
  combineLocal,
  dateKey,
  emptyCalendarForm,
  formToRange,
  formatCalendarDateOnly,
  itemToCalendarForm,
  jiraDeadlineToCalendarRange,
  localDayStartIso,
  pad,
  parseDateKey,
  parseMetadata,
  startOfWeek,
  timeValue,
} from '../lib/calendarFormUtils';
import {
  calendarEntryAccentDot,
  LAYER_LEGEND_DOTS,
  LAYER_TOGGLE_ACTIVE,
  LAYER_TOGGLE_INACTIVE,
} from '../lib/calendarEntryVisuals';

type CalendarRange = 'month' | 'week';
type CalendarEntryKind = 'event' | 'scheduled' | 'jira';

interface CalendarEntry {
  id: string;
  kind: CalendarEntryKind;
  title: string;
  start: Date;
  end: Date;
  allDay: boolean;
  folderIds: string[];
  item?: ItemResponse;
  scheduled?: ScheduledEmailDto;
  location?: string;
  attendees: string[];
  htmlLink?: string;
  meetUrl?: string;
}

interface UpdateEventVariables {
  item: ItemResponse;
  patch: PatchItemRequest;
  start: Date;
  end: Date;
  allDay: boolean;
}

const DRAG_TYPE = 'application/x-workspace-calendar-event';

const DAY_NAMES_VI = ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN'];
const DAY_NAMES_EN = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const WEEK_START_HOUR = 7;
const WEEK_END_HOUR = 21;
const HALF_HOUR_HEIGHT = 28;

const TIMED_ENTRY_CLASSES: Record<CalendarEntryKind, string> = {
  event: 'border-amber-200 bg-amber-50 text-amber-900 dark:border-amber-500/30 dark:bg-amber-500/15 dark:text-amber-200',
  scheduled: 'border-blue-200 bg-blue-50 text-blue-900 dark:border-blue-500/30 dark:bg-blue-500/15 dark:text-blue-200',
  jira: 'border-violet-200 bg-violet-50 text-violet-900 dark:border-violet-500/30 dark:bg-violet-500/15 dark:text-violet-200',
};

/** Cả ngày — tông khác timed để dễ phân biệt trong ô tháng. */
const ALL_DAY_ENTRY_CLASSES: Record<CalendarEntryKind, string> = {
  event: 'border-emerald-200 bg-emerald-50 text-emerald-900 dark:border-emerald-500/30 dark:bg-emerald-500/15 dark:text-emerald-200',
  scheduled: TIMED_ENTRY_CLASSES.scheduled,
  jira: 'border-fuchsia-200 bg-fuchsia-50 text-fuchsia-900 dark:border-fuchsia-500/30 dark:bg-fuchsia-500/15 dark:text-fuchsia-200',
};

function entryChipClasses(entry: CalendarEntry): string {
  const baseClass = entry.allDay ? ALL_DAY_ENTRY_CLASSES[entry.kind] : TIMED_ENTRY_CLASSES[entry.kind];
  if (entry.kind === 'event' && entry.item) {
    const metadata = parseMetadata(entry.item);
    const selfResponse = metadata.selfResponseStatus;
    
    // Nếu từ chối tham gia -> Gạch ngang và mờ đi (declined)
    if (selfResponse === 'declined') {
      return `${baseClass} line-through opacity-45`;
    }
    // Nếu là event được mời nhưng chưa trả lời (needsAction) -> Viền đứt nét hoặc style nhạt hơn
    if (selfResponse === 'needsAction') {
      return `${baseClass} border-dashed border-2`;
    }
    // Nếu là event được mời nhưng trả lời là "có thể" (tentative) -> Hơi mờ hơn chút
    if (selfResponse === 'tentative') {
      return `${baseClass} opacity-80`;
    }
  }
  return baseClass;
}

function isMidnight(date: Date) {
  return date.getHours() === 0 && date.getMinutes() === 0 && date.getSeconds() === 0;
}

function asString(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function asStringArray(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((entry): entry is string => typeof entry === 'string') : [];
}

function parseCalendarDate(value: unknown, fallback: string): Date {
  const raw = asString(value) ?? fallback;
  if (/^\d{4}-\d{2}-\d{2}$/.test(raw)) return parseDateKey(raw);
  const parsed = new Date(raw);
  return Number.isNaN(parsed.getTime()) ? new Date(fallback) : parsed;
}

function itemToEntry(item: ItemResponse): CalendarEntry | null {
  const metadata = parseMetadata(item);

  if (item.type === 'Event') {
    const rawStart = metadata.start ?? item.occurredAt;
    const rawEnd = metadata.end ?? item.dueAt ?? undefined;
    const start = parseCalendarDate(rawStart, item.occurredAt);
    const end = rawEnd ? parseCalendarDate(rawEnd, item.dueAt ?? item.occurredAt) : new Date(start.getTime() + 60 * 60_000);
    const rawStartString = asString(rawStart);
    const rawEndString = asString(rawEnd);
    const explicitAllDay = metadata.allDay === true || metadata.isAllDay === true;
    const dateOnly = Boolean(rawStartString && /^\d{4}-\d{2}-\d{2}$/.test(rawStartString));
    const inferredAllDay = isMidnight(start) && isMidnight(end) && end.getTime() - start.getTime() >= 24 * 60 * 60_000;

    return {
      id: item.id,
      kind: 'event',
      title: item.title,
      start,
      end: end > start ? end : new Date(start.getTime() + 60 * 60_000),
      allDay: explicitAllDay || dateOnly || Boolean(rawEndString && /^\d{4}-\d{2}-\d{2}$/.test(rawEndString)) || inferredAllDay,
      folderIds: item.folderIds ?? [],
      item,
      location: asString(metadata.location),
      attendees: asStringArray(metadata.attendees),
      htmlLink: asString(metadata.htmlLink),
      meetUrl: asString(metadata.meetUrl),
    };
  }

  if (item.type === 'Ticket') {
    const range = jiraDeadlineToCalendarRange(item, metadata);
    if (!range) return null;
    return {
      id: item.id,
      kind: 'jira',
      title: asString(metadata.issueKey) ? `${metadata.issueKey} · ${item.title}` : item.title,
      start: range.start,
      end: range.end,
      allDay: true,
      folderIds: item.folderIds ?? [],
      item,
      attendees: [],
    };
  }

  return null;
}

function scheduledToEntry(email: ScheduledEmailDto): CalendarEntry {
  const start = new Date(email.sendAt);
  return {
    id: email.id,
    kind: 'scheduled',
    title: `Gửi: ${email.subject}`,
    start,
    end: new Date(start.getTime() + 30 * 60_000),
    allDay: false,
    folderIds: [],
    scheduled: email,
    attendees: email.to,
  };
}

function entryOccursOn(entry: CalendarEntry, day: Date) {
  const key = dateKey(day);
  if (!entry.allDay) return dateKey(entry.start) === key;
  const endExclusive = entry.end > entry.start ? entry.end : addDays(entry.start, 1);
  return day >= new Date(entry.start.getFullYear(), entry.start.getMonth(), entry.start.getDate())
    && day < new Date(endExclusive.getFullYear(), endExclusive.getMonth(), endExclusive.getDate());
}

function formatMonthTitle(date: Date, lang: 'vi' | 'en') {
  return lang === 'vi'
    ? `Tháng ${date.getMonth() + 1} · ${date.getFullYear()}`
    : new Intl.DateTimeFormat('en-US', { month: 'long', year: 'numeric' }).format(date);
}

function formatWeekTitle(start: Date, lang: 'vi' | 'en') {
  const end = addDays(start, 6);
  if (lang === 'vi') {
    return start.getMonth() === end.getMonth()
      ? `${start.getDate()} – ${end.getDate()} thg ${end.getMonth() + 1}, ${end.getFullYear()}`
      : `${start.getDate()} thg ${start.getMonth() + 1} – ${end.getDate()} thg ${end.getMonth() + 1}, ${end.getFullYear()}`;
  }
  const short = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric' });
  return `${short.format(start)} – ${short.format(end)}, ${end.getFullYear()}`;
}

function CalendarEntryChip({
  entry,
  compact = false,
  showAllDayLabel = false,
  onOpen,
  onDragStart,
}: {
  entry: CalendarEntry;
  compact?: boolean;
  /** View tháng: hiện "Cả ngày" trước tên (tương tự giờ bắt đầu với event có giờ). */
  showAllDayLabel?: boolean;
  onOpen: (entry: CalendarEntry, event: MouseEvent<HTMLElement>) => void;
  onDragStart: (event: DragEvent, entry: CalendarEntry) => void;
}) {
  const { t } = useI18n();
  const Icon = entry.kind === 'scheduled' ? Mail : entry.kind === 'jira' ? Flag : CalendarDays;
  return (
    <button
      type="button"
      draggable={entry.kind === 'event'}
      onDragStart={event => onDragStart(event, entry)}
      onClick={event => { event.stopPropagation(); onOpen(entry, event); }}
      title={entry.title}
      className={`group flex w-full min-w-0 items-center gap-1.5 overflow-hidden rounded-md border px-1.5 py-1 text-left text-[11px] font-semibold shadow-sm transition hover:brightness-[0.98] ${entryChipClasses(entry)} ${entry.kind === 'event' ? 'cursor-grab active:cursor-grabbing' : 'cursor-pointer'} ${compact ? 'leading-tight' : ''}`}
    >
      <Icon className="h-3 w-3 shrink-0 opacity-75" />
      {entry.allDay && showAllDayLabel ? (
        <span className="shrink-0 text-[10px] font-medium opacity-70">{t('calendar.allDay')}</span>
      ) : !entry.allDay ? (
        <span className="shrink-0 tabular-nums text-[10px] font-medium opacity-70">{timeValue(entry.start)}</span>
      ) : null}
      <span className="truncate">{entry.title}</span>
    </button>
  );
}

export function CalendarPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [searchParams] = useSearchParams();
  const { t, lang } = useI18n();
  const pollMs = usePollingInterval(45_000);
  const folderId = searchParams.get('folder');
  const sourceType = parseSourceType(searchParams.get('type'));
  const googleCalendarOnly = sourceType === 'Event' && !folderId;
  const [range, setRange] = useState<CalendarRange>('month');
  const [cursor, setCursor] = useState(() => new Date());
  const [search, setSearch] = useState('');
  const [layers, setLayers] = useState<Record<CalendarEntryKind, boolean>>({ event: true, scheduled: true, jira: true });
  const [dragOver, setDragOver] = useState<string | null>(null);
  const [selectedEntry, setSelectedEntry] = useState<CalendarEntry | null>(null);
  const [selectedEntryAnchor, setSelectedEntryAnchor] = useState<DOMRect | null>(null);
  const [jiraItemId, setJiraItemId] = useState<string | null>(null);
  const [deleteEntry, setDeleteEntry] = useState<CalendarEntry | null>(null);
  const [editor, setEditor] = useState<{ mode: 'create' | 'edit'; value: CalendarEventFormValue; entry?: CalendarEntry } | null>(null);
  const [moreDay, setMoreDay] = useState<Date | null>(null);

  const { rangeStart, rangeEnd } = useMemo(() => calendarQueryRange(cursor, range), [cursor, range]);
  const occurredFrom = localDayStartIso(rangeStart);
  const occurredTo = localDayStartIso(rangeEnd);

  const calendarItemsKey = ['calendar-items', folderId, googleCalendarOnly ? 'event-only' : 'combined', range, occurredFrom, occurredTo] as const;
  const { data: itemPage, isLoading: itemsLoading, isError: itemsError, isFetching } = useQuery({
    queryKey: calendarItemsKey,
    queryFn: () => itemsApi.getItems({
      folderId: folderId ?? undefined,
      types: googleCalendarOnly ? ['Event'] : ['Event', 'Ticket'],
      occurredFrom,
      occurredTo,
      page: 1,
      limit: 200,
    }),
    staleTime: 0,
    refetchInterval: pollMs,
    refetchIntervalInBackground: true,
  });

  const { data: scheduledPage, isLoading: scheduledLoading } = useQuery({
    queryKey: ['calendar-scheduled-emails'],
    queryFn: () => scheduledEmailsApi.getScheduledEmails(0, 100, 'Pending'),
    enabled: !folderId && !googleCalendarOnly,
    staleTime: 0,
    refetchInterval: pollMs,
    refetchIntervalInBackground: true,
  });

  const { data: folders = [] } = useQuery({
    queryKey: ['folders'],
    queryFn: () => foldersApi.getFolders(false),
  });

  const { data: connections = [] } = useQuery({
    queryKey: ['connections'],
    queryFn: connectionsApi.getConnections,
  });

  const gcalConnections = connections.filter(connection =>
    connection.serviceType.toLowerCase() === 'gcal' && connection.status.toLowerCase() === 'active');
  const currentFolder = folderId ? folders.find(folder => folder.id === folderId) ?? null : null;

  const entries = useMemo(() => {
    const rawItemEntries = (itemPage?.items ?? []).map(itemToEntry).filter((entry): entry is CalendarEntry => entry !== null);
    
    // Deduplicate by externalId (same Google Calendar event synced via multiple connections)
    const seenExternalIds = new Set<string>();
    const itemEntries = rawItemEntries.filter(entry => {
      if (entry.item?.externalId) {
        if (seenExternalIds.has(entry.item.externalId)) return false;
        seenExternalIds.add(entry.item.externalId);
      }
      return true;
    });

    const scheduledEntries = folderId || googleCalendarOnly ? [] : (scheduledPage?.value ?? []).map(scheduledToEntry);
    const queryKind: CalendarEntryKind | null = googleCalendarOnly ? 'event' : null;
    return [...itemEntries, ...scheduledEntries]
      .filter(entry => layers[entry.kind])
      .filter(entry => !queryKind || entry.kind === queryKind)
      .filter(entry => !search.trim() || entry.title.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase()))
      .sort((left, right) => left.start.getTime() - right.start.getTime());
  }, [itemPage, scheduledPage, folderId, googleCalendarOnly, layers, search]);

  const firstConnectionId = gcalConnections[0]?.id ?? '';

  const refreshCalendar = () => {
    queryClient.invalidateQueries({ queryKey: ['calendar-items'] });
    queryClient.invalidateQueries({ queryKey: ['calendar-scheduled-emails'] });
    queryClient.invalidateQueries({ queryKey: ['items'] });
  };

  const createMutation = useMutation({
    mutationFn: async (form: CalendarEventFormValue) => {
      const { start, end } = formToRange(form);
      const times = calendarRangeToApiTimes(start, end, form.allDay);
      const created = await itemsApi.createEvent({
        connectionId: form.connectionId,
        title: form.title,
        start: times.start,
        end: times.end,
        allDay: form.allDay,
        location: form.location.trim() || undefined,
        attendees: form.attendees,
        description: form.description.trim() || undefined,
        driveItemIds: form.driveItemIds.length > 0 ? form.driveItemIds : undefined,
        reminders: form.reminders,
        recurrence: form.recurrence,
      });
      if (folderId) await foldersApi.addItemToFolder(folderId, { itemId: created.id });
      return created;
    },
    onSuccess: () => {
      toast.success(t('calendar.created'));
      setEditor(null);
      refreshCalendar();
      queryClient.invalidateQueries({ queryKey: ['folders'] });
    },
    onError: error => handleApiError(error, t('calendar.createFailed'), { navigate }),
  });

  const updateMutation = useMutation({
    mutationFn: ({ item, patch }: UpdateEventVariables) => itemsApi.patchItem(item.id, patch),
    onMutate: async variables => {
      await queryClient.cancelQueries({ queryKey: calendarItemsKey });
      const previous = queryClient.getQueryData<PagedResult<ItemResponse>>(calendarItemsKey);
      queryClient.setQueryData<PagedResult<ItemResponse>>(calendarItemsKey, current => {
        if (!current) return current;
        return {
          ...current,
          items: current.items.map(item => {
            if (item.id !== variables.item.id) return item;
            const metadata = parseMetadata(item);
            const times = calendarRangeToApiTimes(variables.start, variables.end, variables.allDay);
            metadata.start = times.start;
            metadata.end = times.end;
            metadata.allDay = variables.allDay;
            if (variables.patch.location !== undefined) metadata.location = variables.patch.location;
            if (variables.patch.attendees !== undefined) metadata.attendees = variables.patch.attendees;
            if (variables.patch.description !== undefined) metadata.description = variables.patch.description;
            if (variables.patch.driveItemIds !== undefined) metadata.driveItemIds = variables.patch.driveItemIds;
            return {
              ...item,
              title: variables.patch.title ?? item.title,
              occurredAt: variables.start.toISOString(),
              dueAt: variables.end.toISOString(),
              metadataJson: JSON.stringify(metadata),
            };
          }),
        };
      });
      return { previous };
    },
    onSuccess: () => {
      toast.success(t('calendar.updated'));
      setEditor(null);
      setSelectedEntry(null);
      setSelectedEntryAnchor(null);
      refreshCalendar();
    },
    onError: (error, variables, context) => {
      if (context?.previous) queryClient.setQueryData(calendarItemsKey, context.previous);
      handleApiError(error, t('calendar.updateFailed'), {
        navigate,
        onConflict: () => {
          toast(t('calendar.conflictReload'));
          if (variables.item.connectionId) {
            connectionsApi.syncConnection(variables.item.connectionId)
              .finally(() => refreshCalendar());
          } else {
            refreshCalendar();
          }
        },
      });
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (entry: CalendarEntry) => itemsApi.deleteItem(entry.id),
    onSuccess: () => {
      toast.success(t('calendar.deleted'));
      setDeleteEntry(null);
      setSelectedEntry(null);
      setSelectedEntryAnchor(null);
      refreshCalendar();
    },
    onError: error => handleApiError(error, t('calendar.deleteFailed'), { navigate }),
  });

  const openCreate = (day: Date, startTime = '09:00', allDay = false) => {
    const value = emptyCalendarForm(day, firstConnectionId, startTime);
    value.allDay = allDay;
    setEditor({ mode: 'create', value });
  };

  const openEntry = (entry: CalendarEntry, event?: MouseEvent<HTMLElement>) => {
    setSelectedEntry(entry);
    setSelectedEntryAnchor(event?.currentTarget.getBoundingClientRect() ?? null);
  };

  const submitEditor = (form: CalendarEventFormValue) => {
    if (editor?.mode === 'create') {
      createMutation.mutate(form);
      return;
    }
    if (!editor?.entry?.item) return;
    const { start, end } = formToRange(form);
    updateMutation.mutate({
      item: editor.entry.item,
      start,
      end,
      allDay: form.allDay,
      patch: calendarFormToPatch(form),
    });
  };

  const dragStart = (event: DragEvent, entry: CalendarEntry) => {
    if (entry.kind !== 'event') return;
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData(DRAG_TYPE, entry.id);
    event.dataTransfer.setData('text/plain', entry.id);
  };

  const moveEvent = (entryId: string, targetDate: Date, targetTime?: string, forceAllDay?: boolean) => {
    const entry = entries.find(candidate => candidate.id === entryId && candidate.kind === 'event');
    if (!entry?.item) return;
    const allDay = forceAllDay ?? (targetTime !== undefined ? false : entry.allDay);
    let start: Date;
    let end: Date;
    if (allDay) {
      start = new Date(targetDate.getFullYear(), targetDate.getMonth(), targetDate.getDate());
      const dayCount = entry.allDay ? Math.max(1, Math.round((entry.end.getTime() - entry.start.getTime()) / 86_400_000)) : 1;
      end = addDays(start, dayCount);
    } else {
      const time = targetTime ?? timeValue(entry.start);
      start = combineLocal(dateKey(targetDate), time);
      const duration = entry.allDay ? 60 * 60_000 : Math.max(30 * 60_000, entry.end.getTime() - entry.start.getTime());
      end = new Date(start.getTime() + duration);
    }
    const times = calendarRangeToApiTimes(start, end, allDay);
    updateMutation.mutate({
      item: entry.item,
      start,
      end,
      allDay,
      patch: { start: times.start, end: times.end, allDay },
    });
    setDragOver(null);
  };

  const previousRange = () => setCursor(current => range === 'month' ? addMonths(current, -1) : addDays(current, -7));
  const nextRange = () => setCursor(current => range === 'month' ? addMonths(current, 1) : addDays(current, 7));
  const title = range === 'month' ? formatMonthTitle(cursor, lang) : formatWeekTitle(startOfWeek(cursor), lang);
  const dayNames = lang === 'vi' ? DAY_NAMES_VI : DAY_NAMES_EN;
  const loading = itemsLoading || (!folderId && !googleCalendarOnly && scheduledLoading);

  const calendarSubtitle = useMemo(() => {
    if (loading) return t('common.loading');
    if (folderId) return t('calendar.folderSubtitle');
    if (googleCalendarOnly) return t('calendar.googleSubtitle', { n: entries.length });
    return t('calendar.subtitle', { n: entries.length });
  }, [loading, folderId, googleCalendarOnly, entries.length, t]);

  const entriesForDay = (day: Date) => entries.filter(entry => entryOccursOn(entry, day));

  const renderMonth = () => {
    const first = new Date(cursor.getFullYear(), cursor.getMonth(), 1);
    const offset = (first.getDay() + 6) % 7;
    const gridStart = addDays(first, -offset);
    const dayCount = Math.ceil((offset + new Date(cursor.getFullYear(), cursor.getMonth() + 1, 0).getDate()) / 7) * 7;
    const days = Array.from({ length: dayCount }, (_, index) => addDays(gridStart, index));

    return (
      <div className="min-w-[840px] flex-1">
        <div className="grid grid-cols-7 border-b border-slate-200 bg-slate-50 dark:border-slate-800 dark:bg-slate-900/70">
          {dayNames.map(name => <div key={name} className="px-2 py-2 text-[11px] font-semibold uppercase tracking-wide text-slate-400">{name}</div>)}
        </div>
        <div className="grid grid-cols-7 auto-rows-[minmax(112px,1fr)]">
          {days.map(day => {
            const key = dateKey(day);
            const dayEntries = entriesForDay(day);
            const today = dateKey(new Date()) === key;
            return (
              <div
                key={key}
                onClick={() => openCreate(day)}
                onDragOver={event => {
                  if (!event.dataTransfer.types.includes(DRAG_TYPE)) return;
                  event.preventDefault();
                  setDragOver(`month-${key}`);
                }}
                onDragLeave={() => setDragOver(null)}
                onDrop={event => {
                  event.preventDefault();
                  moveEvent(event.dataTransfer.getData(DRAG_TYPE) || event.dataTransfer.getData('text/plain'), day);
                }}
                className={`min-w-0 cursor-pointer border-b border-r border-slate-100 p-1.5 transition hover:bg-slate-50 dark:border-slate-800 dark:hover:bg-slate-800/50 ${day.getMonth() !== cursor.getMonth() ? 'bg-slate-50/50 dark:bg-slate-950/40' : 'bg-white dark:bg-slate-900'} ${dragOver === `month-${key}` ? 'outline outline-2 -outline-offset-2 outline-dashed outline-brand-500 bg-brand-50/70 dark:bg-brand-500/10' : ''}`}
              >
                <div className={`mb-1 flex h-6 w-6 items-center justify-center rounded-full text-[11.5px] font-semibold ${today ? 'bg-brand-600 text-white' : day.getMonth() === cursor.getMonth() ? 'text-slate-600 dark:text-slate-300' : 'text-slate-400 dark:text-slate-600'}`}>
                  {day.getDate()}
                </div>
                <div className="space-y-1">
                  {dayEntries.slice(0, 3).map(entry => (
                    <CalendarEntryChip key={`${entry.kind}-${entry.id}`} entry={entry} compact showAllDayLabel onOpen={openEntry} onDragStart={dragStart} />
                  ))}
                  {dayEntries.length > 3 && (
                    <button type="button" onClick={event => { event.stopPropagation(); setMoreDay(day); }} className="w-full rounded px-1.5 py-0.5 text-left text-[11px] font-semibold text-brand-600 hover:bg-brand-50 dark:text-brand-300 dark:hover:bg-brand-500/10">
                      {t('calendar.moreItems', { n: dayEntries.length - 3 })}
                    </button>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      </div>
    );
  };

  const renderWeek = () => {
    const weekStart = startOfWeek(cursor);
    const days = Array.from({ length: 7 }, (_, index) => addDays(weekStart, index));
    const slots = Array.from({ length: (WEEK_END_HOUR - WEEK_START_HOUR) * 2 }, (_, index) => index);
    const height = slots.length * HALF_HOUR_HEIGHT;

    return (
      <div className="min-w-[960px] flex-1">
        <div className="sticky top-0 z-20 grid grid-cols-[58px_repeat(7,minmax(120px,1fr))] border-b border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900">
          <div />
          {days.map((day, index) => {
            const today = dateKey(day) === dateKey(new Date());
            return (
              <div key={dateKey(day)} className="border-l border-slate-100 px-2 py-2 text-center dark:border-slate-800">
                <div className="text-[10.5px] font-semibold uppercase tracking-wide text-slate-400">{dayNames[index]}</div>
                <div className={`mx-auto mt-1 flex h-8 w-8 items-center justify-center rounded-full text-[14px] font-bold ${today ? 'bg-brand-600 text-white' : 'text-slate-600 dark:text-slate-300'}`}>{day.getDate()}</div>
              </div>
            );
          })}
        </div>

        <div className="grid grid-cols-[58px_repeat(7,minmax(120px,1fr))] border-b border-slate-200 bg-slate-50/60 dark:border-slate-800 dark:bg-slate-900/60">
          <div className="flex items-center justify-end px-2 text-[10.5px] font-semibold text-slate-400">{t('calendar.allDay')}</div>
          {days.map(day => {
            const key = dateKey(day);
            const allDayEntries = entriesForDay(day).filter(entry => entry.allDay);
            return (
              <div
                key={key}
                onClick={() => openCreate(day, '09:00', true)}
                onDragOver={event => { if (event.dataTransfer.types.includes(DRAG_TYPE)) { event.preventDefault(); setDragOver(`all-${key}`); } }}
                onDragLeave={() => setDragOver(null)}
                onDrop={event => { event.preventDefault(); moveEvent(event.dataTransfer.getData(DRAG_TYPE) || event.dataTransfer.getData('text/plain'), day, undefined, true); }}
                className={`min-h-14 space-y-1 border-l border-slate-100 p-1.5 transition dark:border-slate-800 ${dragOver === `all-${key}` ? 'bg-brand-50 outline outline-2 -outline-offset-2 outline-dashed outline-brand-500 dark:bg-brand-500/10' : ''}`}
              >
                {allDayEntries.map(entry => <CalendarEntryChip key={`${entry.kind}-${entry.id}`} entry={entry} compact onOpen={openEntry} onDragStart={dragStart} />)}
              </div>
            );
          })}
        </div>

        <div className="grid grid-cols-[58px_repeat(7,minmax(120px,1fr))]">
          <div style={{ height }}>
            {slots.map(slot => (
              <div key={slot} style={{ height: HALF_HOUR_HEIGHT }} className="pr-2 text-right text-[10px] tabular-nums text-slate-400">
                {slot % 2 === 0 ? `${pad(WEEK_START_HOUR + slot / 2)}:00` : ''}
              </div>
            ))}
          </div>
          {days.map(day => {
            const key = dateKey(day);
            const timedEntries = entriesForDay(day).filter(entry => !entry.allDay);
            return (
              <div key={key} className="relative border-l border-slate-100 dark:border-slate-800" style={{ height }}>
                {slots.map(slot => {
                  const minutes = WEEK_START_HOUR * 60 + slot * 30;
                  const slotTime = `${pad(Math.floor(minutes / 60))}:${pad(minutes % 60)}`;
                  const slotKey = `slot-${key}-${slotTime}`;
                  return (
                    <button
                      type="button"
                      key={slot}
                      aria-label={`${key} ${slotTime}`}
                      onClick={() => openCreate(day, slotTime)}
                      onDragOver={event => { if (event.dataTransfer.types.includes(DRAG_TYPE)) { event.preventDefault(); setDragOver(slotKey); } }}
                      onDragLeave={() => setDragOver(null)}
                      onDrop={event => { event.preventDefault(); moveEvent(event.dataTransfer.getData(DRAG_TYPE) || event.dataTransfer.getData('text/plain'), day, slotTime, false); }}
                      style={{ top: slot * HALF_HOUR_HEIGHT, height: HALF_HOUR_HEIGHT }}
                      className={`absolute inset-x-0 border-b border-slate-100 transition hover:bg-brand-50/50 dark:border-slate-800 dark:hover:bg-brand-500/5 ${slot % 2 === 0 ? 'border-b-slate-200 dark:border-b-slate-700' : ''} ${dragOver === slotKey ? 'z-10 bg-brand-50 outline outline-2 -outline-offset-2 outline-dashed outline-brand-500 dark:bg-brand-500/10' : ''}`}
                    />
                  );
                })}

                {timedEntries.map(entry => {
                  const startMinutes = entry.start.getHours() * 60 + entry.start.getMinutes();
                  const endMinutes = entry.end.getHours() * 60 + entry.end.getMinutes();
                  const top = ((startMinutes - WEEK_START_HOUR * 60) / 30) * HALF_HOUR_HEIGHT;
                  const entryHeight = Math.max(24, ((Math.max(endMinutes, startMinutes + 30) - startMinutes) / 30) * HALF_HOUR_HEIGHT - 2);
                  if (top < -entryHeight || top >= height) return null;
                  const Icon = entry.kind === 'scheduled' ? Mail : entry.kind === 'jira' ? Flag : CalendarDays;
                  return (
                    <button
                      type="button"
                      key={`${entry.kind}-${entry.id}`}
                      draggable={entry.kind === 'event'}
                      onDragStart={event => dragStart(event, entry)}
                      onClick={event => openEntry(entry, event)}
                      style={{ top: Math.max(0, top), height: entryHeight }}
                      className={`absolute left-1 right-1 z-10 overflow-hidden rounded-lg border px-2 py-1 text-left text-[11px] font-semibold shadow-sm ${entryChipClasses(entry)} ${entry.kind === 'event' ? 'cursor-grab active:cursor-grabbing' : 'cursor-pointer'}`}
                    >
                      <span className="flex items-center gap-1 truncate"><Icon className="h-3 w-3 shrink-0" />{entry.title}</span>
                      <span className="mt-0.5 block text-[10px] font-medium tabular-nums opacity-70">{timeValue(entry.start)}{entry.kind === 'event' ? ` – ${timeValue(entry.end)}` : ''}</span>
                    </button>
                  );
                })}
              </div>
            );
          })}
        </div>
      </div>
    );
  };

  // URL cũ/deep-link không hợp lệ: Email/Jira/Drive không có calendar view.
  if (sourceType && sourceType !== 'Event') {
    return <Navigate to={`/?type=${encodeURIComponent(sourceType)}`} replace />;
  }

  return (
    <div className="flex-1 min-h-0 bg-slate-50 dark:bg-slate-950 overflow-y-auto">
      <div className="max-w-[1400px] mx-auto px-6 py-5">

        <WorkspaceToolbar
          view="calendar"
          folder={currentFolder}
          folderId={folderId}
          subtitle={calendarSubtitle}
          isBackgroundFetching={isFetching && !loading}
          statusFilter={[]}
          onToggleStatusFilter={() => {}}
          typeFilter={[]}
          onToggleTypeFilter={() => {}}
          sourceType={sourceType}
          importantOnly={false}
          onImportantToggle={() => {}}
          tagFilters={[]}
          onToggleTagFilter={() => {}}
          onClearTagFilters={() => {}}
          searchInput={search}
          onSearchChange={setSearch}
        />

        <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <span className="mr-1 text-[11px] font-semibold uppercase tracking-wide text-slate-400">
              {googleCalendarOnly ? t('calendar.scope') : t('calendar.showOnCalendar')}
            </span>
            {googleCalendarOnly ? (
              <span className="inline-flex h-8 items-center gap-1.5 rounded-full border border-amber-200 bg-amber-50 px-3 text-[12.5px] font-semibold text-amber-800 dark:border-amber-500/30 dark:bg-amber-500/15 dark:text-amber-200">
                <span className="inline-flex items-center gap-0.5">
                  <span className="h-2.5 w-2.5 rounded-[3px] bg-amber-500" />
                  <span className="h-2.5 w-2.5 rounded-[3px] bg-emerald-500" />
                </span>
                <CalendarDays className="h-3.5 w-3.5" />{t('calendar.googleScope')}
              </span>
            ) : (['event', 'scheduled', 'jira'] as const)
              .filter(kind => !folderId || kind !== 'scheduled')
              .map(kind => (
                <button
                  key={kind}
                  type="button"
                  onClick={() => setLayers(current => ({ ...current, [kind]: !current[kind] }))}
                  className={`inline-flex h-8 items-center gap-1.5 rounded-full border px-3 text-[12.5px] font-medium transition ${layers[kind] ? LAYER_TOGGLE_ACTIVE[kind] : LAYER_TOGGLE_INACTIVE}`}
                >
                  <span className="inline-flex items-center gap-0.5">
                    {LAYER_LEGEND_DOTS[kind].map(dot => (
                      <span key={dot} className={`h-2.5 w-2.5 rounded-[3px] ${dot}`} />
                    ))}
                  </span>
                  {kind === 'event' ? t('calendar.events') : kind === 'scheduled' ? t('calendar.scheduledEmails') : t('calendar.jiraDeadlines')}
                </button>
              ))}
          </div>
          <button type="button" onClick={() => openCreate(new Date())} className="inline-flex h-9 items-center gap-1.5 rounded-[9px] bg-brand-600 px-3.5 text-[13px] font-semibold text-white shadow-sm hover:bg-brand-700">
            <Plus className="h-4 w-4" />{t('calendar.createEvent')}
          </button>
        </div>

        <div className="mb-2 flex flex-wrap items-center gap-2">
          <button type="button" onClick={previousRange} className="flex h-8 w-8 items-center justify-center rounded-lg border border-slate-200 bg-white text-slate-500 hover:bg-slate-50 dark:border-slate-700 dark:bg-slate-800 dark:hover:bg-slate-700"><ChevronLeft className="h-4 w-4" /></button>
          <button type="button" onClick={nextRange} className="flex h-8 w-8 items-center justify-center rounded-lg border border-slate-200 bg-white text-slate-500 hover:bg-slate-50 dark:border-slate-700 dark:bg-slate-800 dark:hover:bg-slate-700"><ChevronRight className="h-4 w-4" /></button>
          <button type="button" onClick={() => setCursor(new Date())} className="ml-1 h-8 rounded-lg border border-slate-200 bg-white px-3 text-[12.5px] font-semibold text-slate-600 hover:bg-slate-50 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-300 dark:hover:bg-slate-700">{t('calendar.today')}</button>
          <h2 className="ml-1 text-[16px] font-bold tabular-nums">{title}</h2>
          <div className="ml-auto flex items-center gap-1 rounded-[9px] border border-slate-200 bg-white p-[3px] dark:border-slate-700 dark:bg-slate-800">
            {(['month', 'week'] as const).map(value => (
              <button key={value} type="button" onClick={() => setRange(value)} className={`rounded-[6px] px-3 py-1 text-[12.5px] font-medium ${range === value ? 'bg-brand-50 text-brand-700 dark:bg-brand-500/15 dark:text-brand-300' : 'text-slate-500 hover:bg-slate-50 dark:text-slate-400 dark:hover:bg-slate-700'}`}>
                {value === 'month' ? t('calendar.month') : t('calendar.week')}
              </button>
            ))}
          </div>
        </div>

        <div className="flex min-h-[620px] flex-1 overflow-auto rounded-xl border border-slate-200 bg-white shadow-sm dark:border-slate-800 dark:bg-slate-900">
          {loading ? (
            <div className="flex min-h-[620px] w-full items-center justify-center gap-2 text-sm text-slate-400"><Loader2 className="h-5 w-5 animate-spin" />{t('common.loading')}</div>
          ) : itemsError ? (
            <div className="flex min-h-[620px] w-full flex-col items-center justify-center p-8 text-center"><AlertCircle className="mb-3 h-9 w-9 text-rose-500" /><p className="font-semibold">{t('calendar.loadFailed')}</p></div>
          ) : range === 'month' ? renderMonth() : renderWeek()}
        </div>
      </div>

      {editor && (
        <CalendarEventEditorModal
          key={editor.mode === 'edit' ? (editor.entry?.id ?? 'edit') : `create-${editor.value.date}-${editor.value.startTime}`}
          open
          mode={editor.mode}
          initialValue={editor.value}
          connections={gcalConnections}
          allConnections={connections}
          folderName={currentFolder?.name}
          saving={createMutation.isPending || updateMutation.isPending}
          htmlLink={editor.mode === 'edit' ? editor.entry?.htmlLink : undefined}
          onDelete={editor.mode === 'edit' && editor.entry
            ? () => { setEditor(null); setDeleteEntry(editor.entry!); }
            : undefined}
          onClose={() => setEditor(null)}
          onSubmit={submitEditor}
        />
      )}

      {selectedEntry && selectedEntry.kind === 'event' && selectedEntry.item && (
        <EventDetailPopup
          itemId={selectedEntry.id}
          accentDotClass={calendarEntryAccentDot('event', selectedEntry.allDay)}
          anchorRect={selectedEntryAnchor}
          onClose={() => {
            setSelectedEntry(null);
            setSelectedEntryAnchor(null);
          }}
          onEdit={(detailedItem) => {
            const entry = selectedEntry;
            setSelectedEntry(null);
            setSelectedEntryAnchor(null);
            const formVal = itemToCalendarForm(entry.item!);
            formVal.reminders = detailedItem.reminders ?? [];
            formVal.recurrence = detailedItem.recurrence ?? formVal.recurrence ?? [];
            setEditor({ mode: 'edit', value: formVal, entry });
          }}
          onDelete={() => {
            const entry = selectedEntry;
            setSelectedEntry(null);
            setSelectedEntryAnchor(null);
            setDeleteEntry(entry);
          }}
        />
      )}

      {selectedEntry && selectedEntry.kind !== 'event' && (
        <div className="fixed inset-0 z-[8000] flex items-center justify-center bg-slate-900/35 p-4 backdrop-blur-[2px]" onMouseDown={() => { setSelectedEntry(null); setSelectedEntryAnchor(null); }}>
          <div className="w-full max-w-sm rounded-2xl border border-slate-200 bg-white p-5 shadow-2xl dark:border-slate-700 dark:bg-slate-900" onMouseDown={event => event.stopPropagation()}>
            <div className="mb-3 flex items-start justify-between gap-3">
              <div className="grid grid-cols-[14px_minmax(0,1fr)] gap-3">
                <span className={`mt-1.5 h-3 w-3 shrink-0 rounded ${calendarEntryAccentDot(selectedEntry.kind, selectedEntry.allDay)}`} />
                <div>
                <span className={`inline-flex rounded-full border px-2 py-0.5 text-[10.5px] font-semibold ${entryChipClasses(selectedEntry)}`}>
                  {selectedEntry.kind === 'scheduled' ? t('calendar.scheduledEmails') : t('calendar.jiraDeadlines')}
                </span>
                <h3 className="mt-2 text-[16px] font-bold leading-snug text-slate-900 dark:text-slate-100">{selectedEntry.title}</h3>
                </div>
              </div>
              <button type="button" onClick={() => { setSelectedEntry(null); setSelectedEntryAnchor(null); }} className="rounded-lg p-1 text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800"><X className="h-4 w-4" /></button>
            </div>
            <div className="space-y-2.5 text-[12.5px] text-slate-600 dark:text-slate-300">
              <div className="flex gap-2"><Clock3 className="mt-0.5 h-4 w-4 shrink-0 text-slate-400" /><span>{selectedEntry.kind === 'jira' ? formatCalendarDateOnly(selectedEntry.start, lang) : selectedEntry.allDay ? t('calendar.allDay') : `${selectedEntry.start.toLocaleString(lang === 'vi' ? 'vi-VN' : 'en-US')} – ${timeValue(selectedEntry.end)}`}</span></div>
              {selectedEntry.location && <div className="flex gap-2"><MapPin className="mt-0.5 h-4 w-4 shrink-0 text-slate-400" /><span>{selectedEntry.location}</span></div>}
              {selectedEntry.attendees.length > 0 && <div className="flex gap-2"><Users className="mt-0.5 h-4 w-4 shrink-0 text-slate-400" /><span className="break-all">{selectedEntry.attendees.join(', ')}</span></div>}
            </div>
            <div className="mt-5 flex flex-wrap justify-end gap-2">
              {selectedEntry.kind === 'scheduled' && <button type="button" onClick={() => navigate(`/scheduled-emails?open=${selectedEntry.id}`)} className="inline-flex h-9 items-center gap-1.5 rounded-lg bg-blue-600 px-3.5 text-[12.5px] font-semibold text-white hover:bg-blue-700">{t('calendar.openScheduled')}<ExternalLink className="h-3.5 w-3.5" /></button>}
              {selectedEntry.kind === 'jira' && <button type="button" onClick={() => { setJiraItemId(selectedEntry.id); setSelectedEntry(null); setSelectedEntryAnchor(null); }} className="inline-flex h-9 items-center gap-1.5 rounded-lg bg-fuchsia-600 px-3.5 text-[12.5px] font-semibold text-white hover:bg-fuchsia-700">{t('calendar.openJira')}<ExternalLink className="h-3.5 w-3.5" /></button>}
            </div>
          </div>
        </div>
      )}

      <ConfirmDialog
        open={deleteEntry !== null}
        title={deleteEntry?.title}
        message={t('calendar.deleteConfirm')}
        loading={deleteMutation.isPending}
        onCancel={() => setDeleteEntry(null)}
        onConfirm={() => { if (deleteEntry) deleteMutation.mutate(deleteEntry); }}
      />

      {jiraItemId && <ItemDetail itemId={jiraItemId} onClose={() => setJiraItemId(null)} />}

      {/* Popup / Overlay hiển thị tất cả các event trong ngày khi click "Mục khác" */}
      {moreDay && (() => {
        const dayEntries = entriesForDay(moreDay);
        
        const getDayLabel = (date: Date) => {
          const day = date.getDay(); // 0 = CN, 1 = T2, etc.
          if (lang === 'vi') {
            const viMap = ['CN', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7'];
            return viMap[day];
          }
          const enMap = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
          return enMap[day];
        };

        const dayLabel = getDayLabel(moreDay);
        const dateNum = moreDay.getDate();

        return (
          <div className="fixed inset-0 z-[8500] flex items-center justify-center bg-slate-900/35 backdrop-blur-[2px]" onClick={() => setMoreDay(null)}>
            <div 
              className="w-full max-w-sm rounded-3xl border border-slate-200/80 bg-white p-6 shadow-2xl dark:border-slate-800 dark:bg-slate-950 flex flex-col max-h-[70vh] animate-in fade-in zoom-in-95 duration-150 overflow-hidden" 
              onClick={e => e.stopPropagation()}
            >
              {/* Header */}
              <div className="relative flex flex-col items-center pb-4 border-b border-slate-100 dark:border-slate-800">
                <button 
                  type="button" 
                  onClick={() => setMoreDay(null)} 
                  className="absolute right-0 top-0 rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-slate-800 dark:hover:text-slate-200"
                >
                  <X className="h-5 w-5" />
                </button>
                
                <span className="text-[14px] font-bold text-slate-400 dark:text-slate-500 uppercase tracking-wider">{dayLabel}</span>
                <div className="mt-2 flex h-12 w-12 items-center justify-center rounded-full bg-brand-600 text-white text-[20px] font-bold shadow-md shadow-brand-500/10">
                  {dateNum}
                </div>
              </div>

              {/* Event List */}
              <div className="flex-1 overflow-y-auto py-4 space-y-1.5 max-h-[40vh] hide-scrollbar">
                {dayEntries.length > 0 ? (
                  dayEntries.map(entry => {
                    const metadata = entry.item ? parseMetadata(entry.item) : null;
                    const selfResponse = metadata?.selfResponseStatus;
                    const isDeclined = selfResponse === 'declined';
                    
                    let dotClass = 'w-3 h-3 rounded-full shrink-0 ';
                    if (entry.kind === 'event' && selfResponse === 'declined') {
                      dotClass += 'bg-rose-500';
                    } else if (entry.kind === 'event' && selfResponse === 'tentative') {
                      const outline = entry.allDay ? 'border-emerald-500' : 'border-amber-500';
                      dotClass += `bg-transparent border ${outline}`;
                    } else if (entry.kind === 'event' && selfResponse === 'needsAction') {
                      const outline = entry.allDay ? 'border-emerald-500' : 'border-amber-500';
                      dotClass += `bg-transparent border border-dashed ${outline}`;
                    } else {
                      dotClass += calendarEntryAccentDot(entry.kind, entry.allDay);
                    }

                    return (
                      <button
                        key={`${entry.kind}-${entry.id}`}
                        onClick={() => {
                          setMoreDay(null);
                          openEntry(entry);
                        }}
                        className="flex w-full items-center gap-3 rounded-xl px-3 py-2 text-left text-[13px] hover:bg-slate-50 dark:hover:bg-slate-800/50 transition-colors"
                      >
                        <div className={dotClass} />
                        <span className="shrink-0 text-[12px] font-semibold text-slate-400 dark:text-slate-500">
                          {entry.allDay ? t('calendar.allDay') : timeValue(entry.start)}
                        </span>
                        <span className={`truncate font-semibold text-slate-805 dark:text-slate-200 ${isDeclined ? 'line-through opacity-50' : ''}`}>
                          {entry.title}
                        </span>
                      </button>
                    );
                  })
                ) : (
                  <div className="py-8 text-center text-xs text-slate-400 italic">
                    {lang === 'vi' ? 'Không có sự kiện' : 'No events'}
                  </div>
                )}
              </div>
            </div>
          </div>
        );
      })()}
    </div>
  );
}
