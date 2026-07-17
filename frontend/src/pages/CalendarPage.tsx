import { useEffect, useMemo, useRef, useState, type DragEvent, type MouseEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Navigate, useNavigate, useSearchParams } from 'react-router-dom';
import axios from 'axios';
import {
  AlertCircle, CalendarDays, ChevronLeft, ChevronRight, Clock3, ExternalLink,
  Flag, Loader2, Mail, MapPin, Plus, Users, X,
} from 'lucide-react';
import toast from 'react-hot-toast';
import { foldersApi, itemsApi } from '../lib/itemsApi';
import { driveApi } from '../lib/driveApi';
import { scheduledEmailsApi, type ScheduledEmailDto } from '../lib/scheduledEmailsApi';
import { connectionsApi, type ConnectionDto } from '../lib/connectionsApi';
import type { CalendarEventDetailResponse, ItemResponse, PagedResult, PatchItemRequest } from '../types/items';
import type { DrivePermissionRole } from '../types/drive';
import { useI18n } from '../hooks/useI18n';
import { usePollingInterval } from '../hooks/usePollingInterval';
import type { TranslationKey } from '../i18n/translations';
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
import { CalendarInvitationDialog } from '../components/calendar/CalendarInvitationDialog';
import { CalendarGuestNotificationDialog } from '../components/calendar/CalendarGuestNotificationDialog';
import {
  CalendarDriveAccessDialog,
  type DriveAccessChoice,
  type DriveAccessFileNeed,
} from '../components/calendar/CalendarDriveAccessDialog';
import { calendarInvitationsApi, type CalendarInvitation, type CalendarInvitationStatus } from '../lib/calendarInvitationsApi';
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
  LAYER_LEGEND_DOTS,
  LAYER_TOGGLE_ACTIVE,
  LAYER_TOGGLE_INACTIVE,
} from '../lib/calendarEntryVisuals';

type CalendarRange = 'month' | 'week' | 'day' | 'year';
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
  invitation?: CalendarInvitation;
  canModify?: boolean;
}

interface UpdateEventVariables {
  item: ItemResponse;
  patch: PatchItemRequest;
  start: Date;
  end: Date;
  allDay: boolean;
  folderIds?: string[];
}

const DRAG_TYPE = 'application/x-workspace-calendar-event';
const CONFLICT_RECOVERY_DELAY_MS = 2_000;

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

function entryChipClasses(entry: CalendarEntry, connections: ConnectionDto[]): string {
  if (entry.kind === 'event' && entry.item) {
    const metadata = parseMetadata(entry.item);
    const connection = connections.find(c => c.id === entry.item?.connectionId);
    const currentUserEmail = connection?.providerAccountId;
    const organizerEmail = metadata.organizerEmail as string | undefined;
    const isOwner = !organizerEmail || (currentUserEmail && organizerEmail.toLowerCase() === currentUserEmail.toLowerCase());

    const baseClass = isOwner ? ALL_DAY_ENTRY_CLASSES.event : TIMED_ENTRY_CLASSES.event;
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
    return baseClass;
  }
  return entry.allDay ? ALL_DAY_ENTRY_CLASSES[entry.kind] : TIMED_ENTRY_CLASSES[entry.kind];
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

interface CreateEventVariables {
  form: CalendarEventFormValue;
  sendUpdates: boolean;
}

interface PendingGuestSubmit {
  form: CalendarEventFormValue;
  addedCount: number;
  removedCount: number;
}

interface PendingDriveAccessSubmit {
  form: CalendarEventFormValue;
  sendUpdates: boolean;
  needsAccess: DriveAccessFileNeed[];
}

function normalizeAccessEmail(value: string) {
  return value.trim().toLocaleLowerCase();
}

function hasDuplicatePermissionError(error: unknown) {
  return axios.isAxiosError(error) && error.response?.status === 409;
}

function invitationToEntry(invitation: CalendarInvitation): CalendarEntry {
  return {
    id: `invitation-${invitation.id}`,
    kind: 'event',
    title: invitation.title,
    start: new Date(invitation.start),
    end: new Date(invitation.end),
    allDay: invitation.allDay,
    folderIds: [],
    location: invitation.location ?? undefined,
    attendees: invitation.attendees,
    invitation,
  };
}

function entryOccursOn(entry: CalendarEntry, day: Date) {
  const key = dateKey(day);
  if (!entry.allDay) return dateKey(entry.start) === key;
  const endExclusive = entry.end > entry.start ? entry.end : addDays(entry.start, 1);
  return day >= new Date(entry.start.getFullYear(), entry.start.getMonth(), entry.start.getDate())
    && day < new Date(endExclusive.getFullYear(), endExclusive.getMonth(), endExclusive.getDate());
}

function formatMonthTitle(
  date: Date,
  lang: 'vi' | 'en',
  t: (k: TranslationKey, v?: Record<string, string | number>) => string,
) {
  return lang === 'vi'
    ? t('calendar.monthYear', { month: date.getMonth() + 1, year: date.getFullYear() })
    : new Intl.DateTimeFormat('en-US', { month: 'long', year: 'numeric' }).format(date);
}

function formatWeekTitle(
  start: Date,
  lang: 'vi' | 'en',
  t: (k: TranslationKey, v?: Record<string, string | number>) => string,
) {
  const end = addDays(start, 6);
  if (lang === 'vi') {
    return start.getMonth() === end.getMonth()
      ? t('calendar.weekRangeSameMonth', {
          startDay: start.getDate(),
          endDay: end.getDate(),
          month: end.getMonth() + 1,
          year: end.getFullYear(),
        })
      : t('calendar.weekRangeCrossMonth', {
          startDay: start.getDate(),
          startMonth: start.getMonth() + 1,
          endDay: end.getDate(),
          endMonth: end.getMonth() + 1,
          year: end.getFullYear(),
        });
  }
  const short = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric' });
  return `${short.format(start)} – ${short.format(end)}, ${end.getFullYear()}`;
}

function CalendarEntryChip({
  entry,
  connections,
  compact = false,
  showAllDayLabel = false,
  onOpen,
  onDragStart,
}: {
  entry: CalendarEntry;
  connections: ConnectionDto[];
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
      data-calendar-entry={`${entry.kind}-${entry.id}`}
      draggable={entry.kind === 'event' && Boolean(entry.item) && entry.canModify !== false}
      onDragStart={event => onDragStart(event, entry)}
      onClick={event => { event.stopPropagation(); onOpen(entry, event); }}
      title={entry.title}
      className={`group flex w-full min-w-0 items-center gap-1.5 overflow-hidden rounded-md border px-1.5 py-1 text-left text-[11px] font-semibold shadow-sm transition hover:brightness-[0.98] ${entryChipClasses(entry, connections)} ${entry.kind === 'event' && entry.item && entry.canModify !== false ? 'cursor-grab active:cursor-grabbing' : 'cursor-pointer'} ${compact ? 'leading-tight' : ''}`}
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
  const invitationId = searchParams.get('invitation');
  const linkedEventId = searchParams.get('eventId');
  const [range, setRange] = useState<CalendarRange>('month');
  const [cursor, setCursor] = useState(() => new Date());
  const [search, setSearch] = useState('');
  const [layers, setLayers] = useState<Record<CalendarEntryKind, boolean>>({ event: true, scheduled: true, jira: true });
  const [dragOver, setDragOver] = useState<string | null>(null);
  const [selectedEntry, setSelectedEntry] = useState<CalendarEntry | null>(null);
  const [selectedEntryAnchor, setSelectedEntryAnchor] = useState<DOMRect | null>(null);
  const [jiraItemId, setJiraItemId] = useState<string | null>(null);
  const [deleteEntry, setDeleteEntry] = useState<CalendarEntry | null>(null);
  const [editor, setEditor] = useState<{
    mode: 'create' | 'edit';
    value: CalendarEventFormValue;
    entry?: CalendarEntry;
    canInviteOthers?: boolean;
    canManageGuestPermissions?: boolean;
  } | null>(null);
  const [pendingGuestSubmit, setPendingGuestSubmit] = useState<PendingGuestSubmit | null>(null);
  const [pendingDriveAccessSubmit, setPendingDriveAccessSubmit] = useState<PendingDriveAccessSubmit | null>(null);
  const [uploadedDriveItemIds, setUploadedDriveItemIds] = useState<string[]>([]);
  const [driveAccessResolving, setDriveAccessResolving] = useState(false);
  const [driveAccessSaving, setDriveAccessSaving] = useState(false);
  const [moreDay, setMoreDay] = useState<Date | null>(null);
  const linkedEventOpenedRef = useRef<string | null>(null);

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

  const { data: invitations = [] } = useQuery({
    queryKey: ['calendar-invitations', occurredFrom, occurredTo],
    queryFn: () => calendarInvitationsApi.list(occurredFrom, occurredTo),
    enabled: !folderId,
    staleTime: 0,
    refetchInterval: pollMs,
  });

  const { data: linkedInvitation } = useQuery({
    queryKey: ['calendar-invitation', invitationId],
    queryFn: () => calendarInvitationsApi.get(invitationId!),
    enabled: Boolean(invitationId),
  });

  const { data: linkedEventItem } = useQuery({
    queryKey: ['item', linkedEventId],
    queryFn: () => itemsApi.getItemById(linkedEventId!),
    enabled: Boolean(linkedEventId),
  });

  const gcalConnections = connections.filter(connection =>
    connection.serviceType.toLowerCase() === 'gcal' && connection.status.toLowerCase() === 'active');
  const currentFolder = folderId ? folders.find(folder => folder.id === folderId) ?? null : null;

  const getEventAccentDot = (entry: CalendarEntry) => {
    if (entry.kind === 'event' && entry.item) {
      const metadata = parseMetadata(entry.item);
      const connection = connections.find(c => c.id === entry.item?.connectionId);
      const currentUserEmail = connection?.providerAccountId;
      const organizerEmail = metadata.organizerEmail as string | undefined;
      const isOwner = !organizerEmail || (currentUserEmail && organizerEmail.toLowerCase() === currentUserEmail.toLowerCase());
      return isOwner ? 'bg-emerald-500' : 'bg-amber-500';
    }
    if (entry.kind === 'scheduled') return 'bg-blue-500';
    if (entry.kind === 'jira') return 'bg-fuchsia-500';
    return entry.allDay ? 'bg-emerald-500' : 'bg-amber-500';
  };

  const entries = useMemo(() => {
    const invitationByICalUid = new Map(invitations.filter(x => x.iCalUid).map(x => [x.iCalUid!, x]));
    // Google-style: NeedsAction vẫn hiện trên lịch (có noti + có thể RSVP). Chỉ ẩn Declined.
    const rawItemEntries = (itemPage?.items ?? []).map(itemToEntry).filter((entry): entry is CalendarEntry => {
      if (entry === null) return false;
      const iCalUid = entry.item ? asString(parseMetadata(entry.item).iCalUid) : undefined;
      const invitation = iCalUid ? invitationByICalUid.get(iCalUid) : undefined;
      return !invitation || invitation.status !== 'Declined';
    }).map(entry => {
      if (!entry.item || entry.kind !== 'event') return entry;
      const metadata = parseMetadata(entry.item);
      const connection = connections.find(candidate => candidate.id === entry.item?.connectionId);
      const organizerEmail = asString(metadata.organizerEmail);
      const iCalUid = asString(metadata.iCalUid);
      const invitation = iCalUid ? invitationByICalUid.get(iCalUid) : undefined;
      return {
        ...entry,
        // Gắn invitation khi chưa RSVP → click mở dialog Yes/Maybe/No
        invitation: invitation?.status === 'NeedsAction' ? invitation : undefined,
        canModify: !organizerEmail
          || organizerEmail.toLowerCase() === connection?.providerAccountId?.toLowerCase()
          || metadata.guestsCanModify === true,
      };
    });
    
    // Deduplicate by externalId (same Google Calendar event synced via multiple connections)
    const seenExternalIds = new Set<string>();
    const itemEntries = rawItemEntries.filter(entry => {
      if (entry.item?.externalId) {
        if (seenExternalIds.has(entry.item.externalId)) return false;
        seenExternalIds.add(entry.item.externalId);
      }
      return true;
    });

    const seenICalUids = new Set(itemEntries
      .map(entry => entry.item ? asString(parseMetadata(entry.item).iCalUid) : undefined)
      .filter((value): value is string => Boolean(value)));
    const invitationEntries = folderId ? [] : invitations
      .filter(invitation => invitation.status !== 'Declined')
      .filter(invitation => !invitation.iCalUid || !seenICalUids.has(invitation.iCalUid))
      .map(invitationToEntry);
    const scheduledEntries = folderId || googleCalendarOnly ? [] : (scheduledPage?.value ?? []).map(scheduledToEntry);
    const queryKind: CalendarEntryKind | null = googleCalendarOnly ? 'event' : null;
    return [...itemEntries, ...invitationEntries, ...scheduledEntries]
      .filter(entry => layers[entry.kind])
      .filter(entry => !queryKind || entry.kind === queryKind)
      .filter(entry => !search.trim() || entry.title.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase()))
      .sort((left, right) => left.start.getTime() - right.start.getTime());
  }, [itemPage, scheduledPage, invitations, connections, folderId, googleCalendarOnly, layers, search]);

  const linkedEventEntry = useMemo(() => {
    if (!linkedEventItem || linkedEventItem.type !== 'Event') return null;
    return itemToEntry(linkedEventItem);
  }, [linkedEventItem]);

  useEffect(() => {
    if (!linkedEventId) {
      linkedEventOpenedRef.current = null;
      return;
    }
    if (linkedEventOpenedRef.current === linkedEventId) return;

    const entry = entries.find(candidate =>
      candidate.kind === 'event' && candidate.id === linkedEventId && Boolean(candidate.item),
    ) ?? linkedEventEntry;
    if (!entry?.item) return;

    // 1) Đúng tháng trước.
    const onEventMonth =
      range === 'month'
      && cursor.getFullYear() === entry.start.getFullYear()
      && cursor.getMonth() === entry.start.getMonth();
    if (!onEventMonth) {
      // Deferred: tránh setState sync trong effect (react-hooks/set-state-in-effect).
      const navigateToEventMonth = window.setTimeout(() => {
        setRange('month');
        setCursor(entry.start);
      }, 0);
      return () => window.clearTimeout(navigateToEventMonth);
    }

    // 2) Chip đã render → click như user (openEntry tự lấy anchor).
    const clickChip = () => {
      if (linkedEventOpenedRef.current === linkedEventId) return true;
      const chip = document.querySelector<HTMLElement>(`[data-calendar-entry="event-${entry.id}"]`);
      if (!chip) return false;
      linkedEventOpenedRef.current = linkedEventId;
      chip.click();
      return true;
    };

    if (clickChip()) return;
    const timer = window.setTimeout(() => { clickChip(); }, 50);
    return () => window.clearTimeout(timer);
  }, [linkedEventId, linkedEventEntry, entries, range, cursor]);

  const pendingInvitations = invitations.filter(invitation => invitation.status === 'NeedsAction');

  const firstConnectionId = gcalConnections[0]?.id ?? '';

  const refreshCalendar = (itemId?: string) => {
    queryClient.invalidateQueries({ queryKey: ['calendar-items'] });
    queryClient.invalidateQueries({ queryKey: ['calendar-scheduled-emails'] });
    queryClient.invalidateQueries({ queryKey: ['items'] });
    if (itemId) {
      queryClient.invalidateQueries({ queryKey: ['calendar-event-detail', itemId] });
    }
  };

  const createMutation = useMutation({
    mutationFn: async ({ form, sendUpdates }: CreateEventVariables) => {
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
        guestsCanModify: form.guestsCanModify,
        guestsCanInviteOthers: form.guestsCanInviteOthers,
        guestsCanSeeOtherGuests: form.guestsCanSeeOtherGuests,
        sendUpdates,
      });

      const folderIdsToAssign = form.folderIds && form.folderIds.length > 0
        ? form.folderIds
        : (folderId ? [folderId] : []);

      for (const fId of folderIdsToAssign) {
        await foldersApi.addItemToFolder(fId, { itemId: created.id });
      }

      return created;
    },
    onSuccess: () => {
      toast.success(t('calendar.created'));
      setPendingGuestSubmit(null);
      setPendingDriveAccessSubmit(null);
      setUploadedDriveItemIds([]);
      setEditor(null);
      refreshCalendar();
      queryClient.invalidateQueries({ queryKey: ['folders'] });
    },
    onError: error => handleApiError(error, t('calendar.createFailed'), { navigate }),
  });

  const updateMutation = useMutation({
    mutationFn: async ({ item, patch, folderIds }: UpdateEventVariables) => {
      const res = await itemsApi.patchItem(item.id, patch);
      if (folderIds !== undefined) {
        const oldFolderIds = item.folderIds ?? [];
        const added = folderIds.filter(id => !oldFolderIds.includes(id));
        const removed = oldFolderIds.filter(id => !folderIds.includes(id));
        for (const fId of added) {
          await foldersApi.addItemToFolder(fId, { itemId: item.id });
        }
        for (const fId of removed) {
          await foldersApi.removeItemFromFolder(fId, item.id);
        }
      }
      return res;
    },
    onMutate: async variables => {
      await queryClient.cancelQueries({ queryKey: calendarItemsKey });
      await queryClient.cancelQueries({ queryKey: ['calendar-event-detail', variables.item.id] });
      const previous = queryClient.getQueryData<PagedResult<ItemResponse>>(calendarItemsKey);
      const previousDetail = queryClient.getQueryData<CalendarEventDetailResponse>(['calendar-event-detail', variables.item.id]);
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
      queryClient.setQueryData<CalendarEventDetailResponse>(['calendar-event-detail', variables.item.id], current => {
        if (!current) return current;
        const nextDetail: CalendarEventDetailResponse = { ...current };
        if (variables.patch.title !== undefined) nextDetail.title = variables.patch.title;
        if (variables.patch.description !== undefined) nextDetail.description = variables.patch.description;
        if (variables.patch.location !== undefined) nextDetail.location = variables.patch.location;
        if (variables.patch.start !== undefined) nextDetail.start = variables.patch.start;
        if (variables.patch.end !== undefined) nextDetail.end = variables.patch.end;
        if (variables.patch.allDay !== undefined) nextDetail.allDay = variables.patch.allDay;
        if (variables.patch.reminders !== undefined) nextDetail.reminders = variables.patch.reminders;
        if (variables.patch.recurrence !== undefined) nextDetail.recurrence = variables.patch.recurrence;
        if (variables.patch.guestsCanModify !== undefined) nextDetail.guestsCanModify = variables.patch.guestsCanModify;
        if (variables.patch.guestsCanInviteOthers !== undefined) nextDetail.guestsCanInviteOthers = variables.patch.guestsCanInviteOthers;
        if (variables.patch.guestsCanSeeOtherGuests !== undefined) nextDetail.guestsCanSeeOtherGuests = variables.patch.guestsCanSeeOtherGuests;
        if (variables.patch.attendees !== undefined) {
          const previousByEmail = new Map(
            current.attendees.map(attendee => [attendee.email.trim().toLocaleLowerCase(), attendee]),
          );
          nextDetail.attendees = variables.patch.attendees.map(email => {
            const existing = previousByEmail.get(email.trim().toLocaleLowerCase());
            return existing ?? {
              email,
              displayName: null,
              responseStatus: 'needsAction',
              comment: null,
              organizer: false,
            };
          });
        }
        return nextDetail;
      });
      return { previous, previousDetail };
    },
    onSuccess: () => {
      toast.success(t('calendar.updated'));
      setPendingGuestSubmit(null);
      setPendingDriveAccessSubmit(null);
      setUploadedDriveItemIds([]);
      setEditor(null);
      setSelectedEntry(null);
      setSelectedEntryAnchor(null);
      refreshCalendar();
    },
    onError: (error, variables, context) => {
      if (context?.previous) queryClient.setQueryData(calendarItemsKey, context.previous);
      if (context?.previousDetail) {
        queryClient.setQueryData(['calendar-event-detail', variables.item.id], context.previousDetail);
      }
      handleApiError(error, t('calendar.updateFailed'), {
        navigate,
        conflictMessage: t('calendar.conflictReload'),
        onConflict: () => {
          window.setTimeout(() => {
            if (variables.item.connectionId) {
              connectionsApi.syncConnection(variables.item.connectionId)
                .finally(() => refreshCalendar());
            } else {
              refreshCalendar();
            }
          }, CONFLICT_RECOVERY_DELAY_MS);
        },
      });
    },
    onSettled: (_data, _error, variables) => {
      refreshCalendar(variables.item.id);
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

  const respondInvitationMutation = useMutation({
    mutationFn: ({ id, status }: { id: string; status: Exclude<CalendarInvitationStatus, 'NeedsAction'> }) =>
      calendarInvitationsApi.respond(id, status),
    onSuccess: invitation => {
      toast.success(t('calendar.invitationSaved'));
      queryClient.invalidateQueries({ queryKey: ['calendar-invitations'] });
      queryClient.setQueryData(['calendar-invitation', invitation.id], invitation);
      refreshCalendar();
      // Giữ dialog: cập nhật status → ẩn nút RSVP (giống Google hiện phản hồi).
      setSelectedEntry(current => (
        current?.invitation?.id === invitation.id
          ? { ...current, invitation }
          : current
      ));
    },
    onError: error => handleApiError(error, t('calendar.invitationRespondFailed'), { navigate }),
  });

  const openCreate = (day: Date, startTime = '09:00', allDay = false) => {
    const value = emptyCalendarForm(day, firstConnectionId, startTime);
    value.allDay = allDay;
    setUploadedDriveItemIds([]);
    setPendingDriveAccessSubmit(null);
    setEditor({ mode: 'create', value });
  };

  const openEntry = (entry: CalendarEntry, event?: MouseEvent<HTMLElement>) => {
    setSelectedEntry(entry);
    setSelectedEntryAnchor(event?.currentTarget.getBoundingClientRect() ?? null);
  };

  const executeEditorSubmit = (form: CalendarEventFormValue, sendUpdates: boolean) => {
    if (editor?.mode === 'create') {
      createMutation.mutate({ form, sendUpdates });
      return;
    }
    if (!editor?.entry?.item) return;
    const { start, end } = formToRange(form);
    updateMutation.mutate({
      item: editor.entry.item,
      start,
      end,
      allDay: form.allDay,
      patch: { ...calendarFormToPatch(form), sendUpdates },
      folderIds: form.folderIds,
    });
  };

  const resolveDriveAccessNeeds = async (form: CalendarEventFormValue): Promise<DriveAccessFileNeed[]> => {
    const guestEmails = Array.from(new Set(form.attendees.map(normalizeAccessEmail).filter(Boolean)));
    const itemIds = Array.from(new Set(form.driveItemIds));
    if (guestEmails.length === 0 || itemIds.length === 0) return [];

    const uploadedSet = new Set(uploadedDriveItemIds);
    const needs = await Promise.all(itemIds.map(async itemId => {
      const item = await itemsApi.getItemById(itemId).catch(() => null);
      const title = item?.title || itemId;

      if (uploadedSet.has(itemId)) {
        return {
          itemId,
          title,
          missingEmails: guestEmails,
          uploadedThisSession: true,
        } satisfies DriveAccessFileNeed;
      }

      const permissions = await driveApi.listPermissions(itemId);
      const hasLinkAccess = permissions.items.some(permission =>
        permission.isLink || permission.type.toLocaleLowerCase() === 'anyone',
      );
      if (hasLinkAccess) return null;

      const explicitEmails = new Set(
        permissions.items
          .map(permission => normalizeAccessEmail(permission.emailAddress ?? ''))
          .filter(Boolean),
      );
      const missingEmails = guestEmails.filter(email => !explicitEmails.has(email));
      if (missingEmails.length === 0) return null;

      return {
        itemId,
        title,
        missingEmails,
        uploadedThisSession: false,
      } satisfies DriveAccessFileNeed;
    }));

    return needs.filter((need): need is DriveAccessFileNeed => need !== null);
  };

  const continueEditorSubmitAfterGuestChoice = async (form: CalendarEventFormValue, sendUpdates: boolean) => {
    setDriveAccessResolving(true);
    try {
      const needsAccess = await resolveDriveAccessNeeds(form);
      if (needsAccess.length > 0) {
        setPendingDriveAccessSubmit({ form, sendUpdates, needsAccess });
        return;
      }
      executeEditorSubmit(form, sendUpdates);
    } catch (error) {
      handleApiError(error, t('calendar.updateFailed'), { navigate });
    } finally {
      setDriveAccessResolving(false);
    }
  };

  const submitEditor = (form: CalendarEventFormValue) => {
    if (!editor) return;

    const previousGuests = new Set(editor.value.attendees.map(email => email.trim().toLocaleLowerCase()));
    const nextGuests = new Set(form.attendees.map(email => email.trim().toLocaleLowerCase()));
    const addedCount = [...nextGuests].filter(email => !previousGuests.has(email)).length;
    const removedCount = [...previousGuests].filter(email => !nextGuests.has(email)).length;

    if (addedCount > 0 || removedCount > 0) {
      setPendingGuestSubmit({ form, addedCount, removedCount });
      return;
    }

    void continueEditorSubmitAfterGuestChoice(form, true);
  };

  const submitPendingGuestChanges = (sendUpdates: boolean) => {
    if (!pendingGuestSubmit) return;
    const { form } = pendingGuestSubmit;
    setPendingGuestSubmit(null);
    void continueEditorSubmitAfterGuestChoice(form, sendUpdates);
  };

  const submitPendingDriveAccess = async (
    choice: DriveAccessChoice,
    role: DrivePermissionRole,
  ) => {
    if (!pendingDriveAccessSubmit) return;

    setDriveAccessSaving(true);
    try {
      if (choice === 'people') {
        for (const file of pendingDriveAccessSubmit.needsAccess) {
          for (const email of file.missingEmails) {
            try {
              await driveApi.addPermission(file.itemId, { email, role, notify: false });
            } catch (error) {
              if (!hasDuplicatePermissionError(error)) throw error;
            }
          }
        }
      } else if (choice === 'link') {
        for (const file of pendingDriveAccessSubmit.needsAccess) {
          await driveApi.setLinkSharing(file.itemId, { enabled: true, role });
        }
      }

      const { form, sendUpdates } = pendingDriveAccessSubmit;
      setPendingDriveAccessSubmit(null);
      executeEditorSubmit(form, sendUpdates);
    } catch (error) {
      handleApiError(
        error,
        lang === 'vi' ? 'Không cập nhật được quyền truy cập Drive.' : 'Could not update Drive access.',
        { navigate },
      );
    } finally {
      setDriveAccessSaving(false);
    }
  };

  const dragStart = (event: DragEvent, entry: CalendarEntry) => {
    if (entry.kind !== 'event' || entry.canModify === false) return;
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

  const previousRange = () => setCursor(current => {
    if (range === 'month') return addMonths(current, -1);
    if (range === 'week') return addDays(current, -7);
    if (range === 'day') return addDays(current, -1);
    return addMonths(current, -12); // year view
  });
  const nextRange = () => setCursor(current => {
    if (range === 'month') return addMonths(current, 1);
    if (range === 'week') return addDays(current, 7);
    if (range === 'day') return addDays(current, 1);
    return addMonths(current, 12); // year view
  });
  const title = useMemo(() => {
    if (range === 'month') return formatMonthTitle(cursor, lang, t);
    if (range === 'week') return formatWeekTitle(startOfWeek(cursor), lang, t);
    if (range === 'day') {
      const weekdayKeys = [
        'calendar.weekdaySunFull',
        'calendar.weekdayMonFull',
        'calendar.weekdayTueFull',
        'calendar.weekdayWedFull',
        'calendar.weekdayThuFull',
        'calendar.weekdayFriFull',
        'calendar.weekdaySatFull',
      ] as const;
      return lang === 'vi'
        ? t('calendar.dayTitleVi', {
            weekday: t(weekdayKeys[cursor.getDay()]),
            day: cursor.getDate(),
            month: cursor.getMonth() + 1,
            year: cursor.getFullYear(),
          })
        : cursor.toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' });
    }
    return lang === 'vi' ? t('calendar.yearN', { year: cursor.getFullYear() }) : `${cursor.getFullYear()}`;
  }, [cursor, range, lang, t]);
  const dayNames = [
    t('calendar.dowMon'),
    t('calendar.dowTue'),
    t('calendar.dowWed'),
    t('calendar.dowThu'),
    t('calendar.dowFri'),
    t('calendar.dowSat'),
    t('calendar.dowSun'),
  ];
  const loading = itemsLoading || (!folderId && !googleCalendarOnly && scheduledLoading);

  const calendarSubtitle = useMemo(() => {
    if (loading) return t('common.loading');
    if (folderId) return t('calendar.folderSubtitle');
    if (googleCalendarOnly) return t('calendar.googleSubtitle', { n: entries.length });
    return t('calendar.subtitle', { n: entries.length });
  }, [loading, folderId, googleCalendarOnly, entries.length, t]);

  const activeInvitation = selectedEntry?.invitation ?? linkedInvitation ?? null;
  const closeLinkedEvent = () => {
    setSelectedEntry(null);
    setSelectedEntryAnchor(null);
    if (linkedEventId) {
      const next = new URLSearchParams(searchParams);
      next.delete('eventId');
      navigate({ search: next.toString() }, { replace: true });
    }
  };

  const closeInvitation = () => {
    setSelectedEntry(null);
    setSelectedEntryAnchor(null);
    if (invitationId) {
      const next = new URLSearchParams(searchParams);
      next.delete('invitation');
      navigate({ search: next.toString() }, { replace: true });
    }
  };

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
                    <CalendarEntryChip key={`${entry.kind}-${entry.id}`} entry={entry} connections={connections} compact showAllDayLabel onOpen={openEntry} onDragStart={dragStart} />
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
                {allDayEntries.map(entry => <CalendarEntryChip key={`${entry.kind}-${entry.id}`} entry={entry} connections={connections} compact onOpen={openEntry} onDragStart={dragStart} />)}
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
                      data-calendar-entry={`${entry.kind}-${entry.id}`}
                      draggable={entry.kind === 'event'}
                      onDragStart={event => dragStart(event, entry)}
                      onClick={event => openEntry(entry, event)}
                      style={{ top: Math.max(0, top), height: entryHeight }}
                      className={`absolute left-1 right-1 z-10 overflow-hidden rounded-lg border px-2 py-1 text-left text-[11px] font-semibold shadow-sm ${entryChipClasses(entry, connections)} ${entry.kind === 'event' ? 'cursor-grab active:cursor-grabbing' : 'cursor-pointer'}`}
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

  const renderDay = () => {
    const slots = Array.from({ length: (WEEK_END_HOUR - WEEK_START_HOUR) * 2 }, (_, index) => index);
    const height = slots.length * HALF_HOUR_HEIGHT;
    const todayKey = dateKey(cursor);
    const allDayEntries = entriesForDay(cursor).filter(entry => entry.allDay);
    const timedEntries = entriesForDay(cursor).filter(entry => !entry.allDay);

    return (
      <div className="flex-1 min-w-[320px]">
        {/* All day section */}
        <div className="grid grid-cols-[58px_1fr] border-b border-slate-200 bg-slate-50/60 dark:border-slate-800 dark:bg-slate-900/60">
          <div className="flex items-center justify-end px-2 text-[10.5px] font-semibold text-slate-400">{t('calendar.allDay')}</div>
          <div
            onClick={() => openCreate(cursor, '09:00', true)}
            onDragOver={event => { if (event.dataTransfer.types.includes(DRAG_TYPE)) { event.preventDefault(); setDragOver(`all-${todayKey}`); } }}
            onDragLeave={() => setDragOver(null)}
            onDrop={event => { event.preventDefault(); moveEvent(event.dataTransfer.getData(DRAG_TYPE) || event.dataTransfer.getData('text/plain'), cursor, undefined, true); }}
            className={`min-h-14 space-y-1 p-2 transition border-l border-slate-100 dark:border-slate-800 ${dragOver === `all-${todayKey}` ? 'bg-brand-50 outline outline-2 -outline-offset-2 outline-dashed outline-brand-500 dark:bg-brand-500/10' : ''}`}
          >
            {allDayEntries.map(entry => (
              <CalendarEntryChip key={`${entry.kind}-${entry.id}`} entry={entry} connections={connections} compact onOpen={openEntry} onDragStart={dragStart} />
            ))}
          </div>
        </div>

        {/* Timed section */}
        <div className="grid grid-cols-[58px_1fr]">
          <div style={{ height }}>
            {slots.map(slot => (
              <div key={slot} style={{ height: HALF_HOUR_HEIGHT }} className="pr-2 text-right text-[10px] tabular-nums text-slate-400">
                {slot % 2 === 0 ? `${pad(WEEK_START_HOUR + slot / 2)}:00` : ''}
              </div>
            ))}
          </div>
          <div className="relative border-l border-slate-100 dark:border-slate-800" style={{ height }}>
            {slots.map(slot => {
              const minutes = WEEK_START_HOUR * 60 + slot * 30;
              const slotTime = `${pad(Math.floor(minutes / 60))}:${pad(minutes % 60)}`;
              const slotKey = `slot-${todayKey}-${slotTime}`;
              return (
                <button
                  type="button"
                  key={slot}
                  aria-label={`${todayKey} ${slotTime}`}
                  onClick={() => openCreate(cursor, slotTime)}
                  onDragOver={event => { if (event.dataTransfer.types.includes(DRAG_TYPE)) { event.preventDefault(); setDragOver(slotKey); } }}
                  onDragLeave={() => setDragOver(null)}
                  onDrop={event => { event.preventDefault(); moveEvent(event.dataTransfer.getData(DRAG_TYPE) || event.dataTransfer.getData('text/plain'), cursor, slotTime, false); }}
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
                  data-calendar-entry={`${entry.kind}-${entry.id}`}
                  draggable={entry.kind === 'event'}
                  onDragStart={event => dragStart(event, entry)}
                  onClick={event => openEntry(entry, event)}
                  style={{ top: Math.max(0, top), height: entryHeight }}
                  className={`absolute left-2 right-2 z-10 overflow-hidden rounded-lg border px-3 py-1.5 text-left text-[11.5px] font-semibold shadow-sm ${entryChipClasses(entry, connections)} ${entry.kind === 'event' ? 'cursor-grab active:cursor-grabbing' : 'cursor-pointer'}`}
                >
                  <span className="flex items-center gap-1 truncate"><Icon className="h-3 w-3 shrink-0" />{entry.title}</span>
                  <span className="mt-0.5 block text-[10.5px] font-medium tabular-nums opacity-70">{timeValue(entry.start)} – {timeValue(entry.end)}</span>
                </button>
              );
            })}
          </div>
        </div>
      </div>
    );
  };

  const renderYear = () => {
    const year = cursor.getFullYear();
    const months = Array.from({ length: 12 }, (_, i) => i);

    return (
      <div className="grid grid-cols-1 gap-6 p-5 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 bg-slate-50 dark:bg-slate-950 w-full">
        {months.map(monthIndex => {
          const first = new Date(year, monthIndex, 1);
          const offset = (first.getDay() + 6) % 7;
          const gridStart = addDays(first, -offset);
          const daysInMonth = new Date(year, monthIndex + 1, 0).getDate();
          const dayCount = Math.ceil((offset + daysInMonth) / 7) * 7;
          const days = Array.from({ length: dayCount }, (_, idx) => addDays(gridStart, idx));
          
          const monthLabel = lang === 'vi'
            ? t('calendar.monthN', { n: monthIndex + 1 })
            : new Intl.DateTimeFormat('en-US', { month: 'long' }).format(first);

          return (
            <div key={monthIndex} className="rounded-xl border border-slate-100 bg-white p-3 shadow-sm dark:border-slate-800 dark:bg-slate-900">
              <h3 className="mb-2 text-center text-xs font-bold text-slate-700 dark:text-slate-200">{monthLabel}</h3>
              <div className="grid grid-cols-7 gap-y-1 text-center text-[9px] font-semibold text-slate-400 uppercase tracking-wider mb-1">
                {dayNames.map((n, idx) => <div key={`${n}-${idx}`}>{n}</div>)}
              </div>
              <div className="grid grid-cols-7 gap-y-1 text-center">
                {days.map((day, idx) => {
                  const isCurrentMonth = day.getMonth() === monthIndex;
                  const key = dateKey(day);
                  const dayEntries = entriesForDay(day);
                  const hasEvents = dayEntries.length > 0;
                  const today = dateKey(new Date()) === key;
                  
                  let dayClass = 'text-[10px] py-1 rounded-md font-medium select-none cursor-pointer ';
                  if (!isCurrentMonth) {
                    dayClass += 'text-slate-300 dark:text-slate-700 pointer-events-none';
                  } else if (today) {
                    dayClass += 'bg-brand-600 text-white font-bold';
                  } else if (hasEvents) {
                    dayClass += 'bg-brand-50 text-brand-700 dark:bg-brand-500/10 dark:text-brand-300 font-semibold';
                  } else {
                    dayClass += 'text-slate-600 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800';
                  }

                  return (
                    <div
                      key={idx}
                      className={dayClass}
                      onClick={() => {
                        if (isCurrentMonth) {
                          setCursor(day);
                          setRange('day');
                        }
                      }}
                      title={hasEvents ? `${dayEntries.length} sự kiện` : undefined}
                    >
                      {isCurrentMonth ? day.getDate() : ''}
                    </div>
                  );
                })}
              </div>
            </div>
          );
        })}
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

        {pendingInvitations.length > 0 && (
          <button
            type="button"
            onClick={() => setSelectedEntry(invitationToEntry(pendingInvitations[0]))}
            className="mb-3 inline-flex items-center gap-2 rounded-xl border border-brand-200 bg-brand-50 px-3.5 py-2 text-sm font-semibold text-brand-700 shadow-sm transition hover:bg-brand-100 dark:border-brand-500/30 dark:bg-brand-500/10 dark:text-brand-200 dark:hover:bg-brand-500/15"
          >
            <Users className="h-4 w-4" />
            {t('calendar.pendingInvitations', { n: pendingInvitations.length })}
          </button>
        )}

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
            {(['day', 'week', 'month', 'year'] as const).map(value => (
              <button key={value} type="button" onClick={() => setRange(value)} className={`rounded-[6px] px-3 py-1 text-[12.5px] font-medium ${range === value ? 'bg-brand-50 text-brand-700 dark:bg-brand-500/15 dark:text-brand-300' : 'text-slate-500 hover:bg-slate-50 dark:text-slate-400 dark:hover:bg-slate-700'}`}>
                {value === 'day' ? t('calendar.day') :
                 value === 'week' ? t('calendar.week') :
                 value === 'month' ? t('calendar.month') :
                 t('calendar.year')}
              </button>
            ))}
          </div>
        </div>

        <div className="flex min-h-[620px] flex-1 overflow-auto rounded-xl border border-slate-200 bg-white shadow-sm dark:border-slate-800 dark:bg-slate-900">
          {loading ? (
            <div className="flex min-h-[620px] w-full items-center justify-center gap-2 text-sm text-slate-400"><Loader2 className="h-5 w-5 animate-spin" />{t('common.loading')}</div>
          ) : itemsError ? (
            <div className="flex min-h-[620px] w-full flex-col items-center justify-center p-8 text-center"><AlertCircle className="mb-3 h-9 w-9 text-rose-500" /><p className="font-semibold">{t('calendar.loadFailed')}</p></div>
          ) : range === 'day' ? renderDay() :
              range === 'week' ? renderWeek() :
              range === 'month' ? renderMonth() :
              renderYear()}
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
          saving={createMutation.isPending || updateMutation.isPending || driveAccessResolving}
          htmlLink={editor.mode === 'edit' ? editor.entry?.htmlLink : undefined}
          onDelete={editor.mode === 'edit' && editor.entry && editor.canManageGuestPermissions !== false
            ? () => { setEditor(null); setDeleteEntry(editor.entry!); }
            : undefined}
          canInviteOthers={editor.canInviteOthers}
          canManageGuestPermissions={editor.canManageGuestPermissions}
          onClose={() => {
            setPendingGuestSubmit(null);
            setPendingDriveAccessSubmit(null);
            setUploadedDriveItemIds([]);
            setEditor(null);
          }}
          onDrivePickerSelectMeta={meta => {
            if (meta.uploadedThisSessionIds.length === 0) return;
            setUploadedDriveItemIds(current => (
              Array.from(new Set([...current, ...meta.uploadedThisSessionIds]))
            ));
          }}
          onSubmit={submitEditor}
        />
      )}

      <CalendarGuestNotificationDialog
        open={pendingGuestSubmit !== null}
        addedCount={pendingGuestSubmit?.addedCount ?? 0}
        removedCount={pendingGuestSubmit?.removedCount ?? 0}
        saving={createMutation.isPending || updateMutation.isPending}
        onBack={() => setPendingGuestSubmit(null)}
        onDontSend={() => submitPendingGuestChanges(false)}
        onSend={() => submitPendingGuestChanges(true)}
      />

      <CalendarDriveAccessDialog
        open={pendingDriveAccessSubmit !== null}
        files={pendingDriveAccessSubmit?.needsAccess ?? []}
        guests={pendingDriveAccessSubmit?.form.attendees ?? []}
        saving={driveAccessSaving}
        onCancel={() => setPendingDriveAccessSubmit(null)}
        onSave={(choice, role) => void submitPendingDriveAccess(choice, role)}
      />

      {selectedEntry && selectedEntry.kind === 'event' && selectedEntry.item && selectedEntry.invitation?.status !== 'NeedsAction' && (
        <EventDetailPopup
          itemId={selectedEntry.id}
          accentDotClass={getEventAccentDot(selectedEntry)}
          anchorRect={selectedEntryAnchor}
          onClose={closeLinkedEvent}
          onEdit={(detailedItem) => {
            const entry = selectedEntry;
            closeLinkedEvent();
            const formVal = itemToCalendarForm(entry.item!);
            formVal.reminders = detailedItem.reminders ?? [];
            formVal.recurrence = detailedItem.recurrence ?? formVal.recurrence ?? [];
            if (detailedItem.canSeeGuestList) {
              formVal.attendees = detailedItem.attendees
                .filter(attendee => !attendee.organizer && attendee.email.trim().length > 0)
                .map(attendee => attendee.email);
            }
            formVal.guestsCanModify = detailedItem.guestsCanModify;
            formVal.guestsCanInviteOthers = detailedItem.guestsCanInviteOthers;
            formVal.guestsCanSeeOtherGuests = detailedItem.guestsCanSeeOtherGuests;
            setUploadedDriveItemIds([]);
            setPendingDriveAccessSubmit(null);
            setEditor({
              mode: 'edit',
              value: formVal,
              entry,
              canInviteOthers: detailedItem.canInviteOthers,
              canManageGuestPermissions: detailedItem.isOrganizer,
            });
          }}
          onDelete={() => {
            const entry = selectedEntry;
            closeLinkedEvent();
            setDeleteEntry(entry);
          }}
        />
      )}

      {activeInvitation && (
        <CalendarInvitationDialog
          invitation={activeInvitation}
          saving={respondInvitationMutation.isPending}
          onClose={closeInvitation}
          onRespond={status => respondInvitationMutation.mutate({ id: activeInvitation.id, status })}
        />
      )}

      {selectedEntry && selectedEntry.kind !== 'event' && (
        <div className="fixed inset-0 z-[8000] flex items-center justify-center bg-slate-900/35 p-4 backdrop-blur-[2px]" onMouseDown={() => { setSelectedEntry(null); setSelectedEntryAnchor(null); }}>
          <div className="w-full max-w-sm rounded-2xl border border-slate-200 bg-white p-5 shadow-2xl dark:border-slate-700 dark:bg-slate-900" onMouseDown={event => event.stopPropagation()}>
            <div className="mb-3 flex items-start justify-between gap-3">
              <div className="grid grid-cols-[14px_minmax(0,1fr)] gap-3">
                <span className={`mt-1.5 h-3 w-3 shrink-0 rounded ${getEventAccentDot(selectedEntry)}`} />
                <div>
                <span className={`inline-flex rounded-full border px-2 py-0.5 text-[10.5px] font-semibold ${entryChipClasses(selectedEntry, connections)}`}>
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
          const keys = [
            'calendar.dowSun',
            'calendar.dowMon',
            'calendar.dowTue',
            'calendar.dowWed',
            'calendar.dowThu',
            'calendar.dowFri',
            'calendar.dowSat',
          ] as const;
          return t(keys[date.getDay()]);
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
                      const color = getEventAccentDot(entry);
                      const borderClass = color === 'bg-emerald-500' ? 'border-emerald-500' : 'border-amber-500';
                      dotClass += `bg-transparent border ${borderClass}`;
                    } else if (entry.kind === 'event' && selfResponse === 'needsAction') {
                      const color = getEventAccentDot(entry);
                      const borderClass = color === 'bg-emerald-500' ? 'border-emerald-500' : 'border-amber-500';
                      dotClass += `bg-transparent border border-dashed ${borderClass}`;
                    } else {
                      dotClass += getEventAccentDot(entry);
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
                    {t('calendar.noEvents')}
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
