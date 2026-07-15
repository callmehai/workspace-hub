import type { ItemResponse, PatchItemRequest } from '../types/items';
import type { CalendarEventFormValue, CalendarDriveAttachmentSnapshot } from '../components/calendar/CalendarEventEditorModal';
import type { ConnectionDto } from './connectionsApi';

export function pad(value: number) {
  return String(value).padStart(2, '0');
}

export function dateKey(date: Date) {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function parseDateKey(value: string) {
  const [year, month, day] = value.split('-').map(Number);
  return new Date(year, month - 1, day);
}

export function addDays(date: Date, amount: number) {
  const result = new Date(date);
  result.setDate(result.getDate() + amount);
  return result;
}

export function combineLocal(date: string, time: string) {
  const [year, month, day] = date.split('-').map(Number);
  const [hour, minute] = time.split(':').map(Number);
  return new Date(year, month - 1, day, hour, minute, 0, 0);
}

export function timeValue(date: Date) {
  return `${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

export function parseMetadata(item: ItemResponse): Record<string, unknown> {
  try {
    return item.metadataJson ? JSON.parse(item.metadataJson) as Record<string, unknown> : {};
  } catch {
    return {};
  }
}

function asString(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function asStringArray(value: unknown): string[] {
  if (Array.isArray(value)) return value.filter((entry): entry is string => typeof entry === 'string');
  return [];
}

function parseCalendarDate(value: unknown, fallback: string): Date {
  const raw = asString(value) ?? fallback;
  if (/^\d{4}-\d{2}-\d{2}$/.test(raw)) return parseDateKey(raw);
  const parsed = new Date(raw);
  return Number.isNaN(parsed.getTime()) ? new Date(fallback) : parsed;
}

function asDriveAttachments(value: unknown): CalendarDriveAttachmentSnapshot[] {
  if (!Array.isArray(value)) return [];
  return value.flatMap(entry => {
    if (!entry || typeof entry !== 'object') return [];
    const row = entry as Record<string, unknown>;
    const fileId = asString(row.fileId) ?? asString(row.FileId);
    if (!fileId) return [];
    return [{
      fileId,
      title: asString(row.title) ?? asString(row.Title) ?? null,
      mimeType: asString(row.mimeType) ?? asString(row.MimeType) ?? null,
      fileUrl: asString(row.fileUrl) ?? asString(row.FileUrl) ?? null,
    }];
  });
}

/** URL mở file Drive từ Item (metadata.webViewLink hoặc externalId). */
export function driveItemOpenUrl(item: { externalId?: string | null; metadataJson?: string | null }): string | null {
  if (item.metadataJson) {
    try {
      const meta = JSON.parse(item.metadataJson) as Record<string, unknown>;
      const link = asString(meta.webViewLink) ?? asString(meta.WebViewLink);
      if (link) return link;
    } catch { /* ignore */ }
  }
  if (item.externalId) return `https://drive.google.com/file/d/${item.externalId}/view`;
  return null;
}

/** Gmail connection để gợi ý contact — ưu tiên cùng Google account với GCal đang chọn. */
export function resolveGmailSuggestConnection(
  allConnections: ConnectionDto[],
  gcalConnectionId: string,
): string | undefined {
  const activeGmail = allConnections.filter(
    c => c.serviceType.toLowerCase() === 'gmail' && c.status.toLowerCase() === 'active',
  );
  if (activeGmail.length === 0) return undefined;
  const gcal = allConnections.find(c => c.id === gcalConnectionId);
  if (gcal) {
    const sameAccount = activeGmail.find(g => g.providerAccountId === gcal.providerAccountId);
    if (sameAccount) return sameAccount.id;
  }
  return activeGmail[0]?.id;
}

export function formatCalendarDateOnly(date: Date, lang: 'vi' | 'en') {
  return date.toLocaleDateString(lang === 'vi' ? 'vi-VN' : 'en-US', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  });
}

/** Jira duedate = date-only — luôn all-day theo ngày lịch; không dùng dueAt (end exclusive UTC). */
export function jiraDeadlineToCalendarRange(
  item: ItemResponse,
  metadata: Record<string, unknown>,
): { start: Date; end: Date } | null {
  const dueDateOnly = asString(metadata.dueDate) ?? asString(metadata.duedate);
  if (!dueDateOnly && !item.dueAt) return null;

  if (dueDateOnly && /^\d{4}-\d{2}-\d{2}$/.test(dueDateOnly)) {
    const start = parseDateKey(dueDateOnly);
    return { start, end: addDays(start, 1) };
  }

  // Fallback: BE lưu OccurredAt = UTC midnight ngày due — lấy phần ngày UTC, tránh lệch +7h local.
  const anchor = item.occurredAt ?? item.dueAt;
  if (!anchor) return null;
  const d = new Date(anchor);
  const start = parseDateKey(`${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`);
  return { start, end: addDays(start, 1) };
}

export function formatJiraDueDate(
  item: ItemResponse,
  metadata: Record<string, unknown>,
  lang: 'vi' | 'en',
): string | null {
  const range = jiraDeadlineToCalendarRange(item, metadata);
  return range ? formatCalendarDateOnly(range.start, lang) : null;
}

export function emptyCalendarForm(date: Date, connectionId = '', startTime = '09:00'): CalendarEventFormValue {
  const [hour, minute] = startTime.split(':').map(Number);
  const endMinutes = Math.min(hour * 60 + minute + 60, 23 * 60 + 30);
  return {
    connectionId,
    title: '',
    date: dateKey(date),
    endDate: dateKey(date),
    allDay: false,
    startTime,
    endTime: `${pad(Math.floor(endMinutes / 60))}:${pad(endMinutes % 60)}`,
    location: '',
    attendees: [],
    description: '',
    driveItemIds: [],
    driveAttachments: [],
    reminders: [
      { reminderType: 'GooglePopup', offsetValue: 30, offsetUnit: 'Minutes' },
      { reminderType: 'InApp', offsetValue: 30, offsetUnit: 'Minutes' },
    ],
    recurrence: [],
    guestsCanModify: false,
    guestsCanInviteOthers: true,
    guestsCanSeeOtherGuests: true,
  };
}

export function itemToCalendarForm(item: ItemResponse): CalendarEventFormValue {
  const metadata = parseMetadata(item);
  const rawStart = metadata.start ?? item.occurredAt;
  const rawEnd = metadata.end ?? item.dueAt ?? item.occurredAt;
  const start = parseCalendarDate(rawStart, item.occurredAt);
  const end = parseCalendarDate(rawEnd, item.dueAt ?? item.occurredAt);
  const explicitAllDay = metadata.allDay === true || metadata.isAllDay === true;
  const rawStartString = asString(rawStart);
  const dateOnly = Boolean(rawStartString && /^\d{4}-\d{2}-\d{2}$/.test(rawStartString));
  const displayEnd = (explicitAllDay || dateOnly) ? (end > start ? addDays(end, -1) : start) : end;

  return {
    connectionId: item.connectionId ?? '',
    title: item.title,
    date: dateKey(start),
    endDate: dateKey(displayEnd > start ? displayEnd : start),
    allDay: explicitAllDay || dateOnly,
    startTime: timeValue(start),
    endTime: timeValue(end > start ? end : new Date(start.getTime() + 60 * 60_000)),
    location: asString(metadata.location) ?? '',
    attendees: asStringArray(metadata.attendees),
    description: asString(metadata.description) ?? item.snippet ?? '',
    driveItemIds: asStringArray(metadata.driveItemIds),
    driveAttachments: asDriveAttachments(metadata.driveAttachments),
    reminders: item.reminders ?? [],
    recurrence: asStringArray(metadata.recurrence),
    guestsCanModify: metadata.guestsCanModify === true,
    guestsCanInviteOthers: metadata.guestsCanInviteOthers !== false,
    guestsCanSeeOtherGuests: metadata.guestsCanSeeOtherGuests !== false,
  };
}

export function formToRange(form: CalendarEventFormValue) {
  const endD = form.endDate ? parseDateKey(form.endDate) : parseDateKey(form.date);
  if (form.allDay) {
    const start = parseDateKey(form.date);
    const end = endD >= start ? endD : start;
    return { start, end: addDays(end, 1) };
  }
  const start = combineLocal(form.date, form.startTime);
  const rawEnd = combineLocal(form.endDate || form.date, form.endTime);
  const end = rawEnd >= start ? rawEnd : new Date(start.getTime() + 60 * 60_000);
  return { start, end };
}

/** All-day events use date-only strings (Google Calendar contract); timed events use ISO UTC. */
export function calendarRangeToApiTimes(start: Date, end: Date, allDay: boolean) {
  if (allDay) {
    return { start: dateKey(start), end: dateKey(end) };
  }
  return { start: start.toISOString(), end: end.toISOString() };
}

export function calendarFormToPatch(form: CalendarEventFormValue): PatchItemRequest {
  const { start, end } = formToRange(form);
  const times = calendarRangeToApiTimes(start, end, form.allDay);
  return {
    title: form.title,
    start: times.start,
    end: times.end,
    allDay: form.allDay,
    location: form.location.trim(),
    attendees: form.attendees,
    description: form.description.trim(),
    driveItemIds: form.driveItemIds,
    reminders: form.reminders,
    recurrence: form.recurrence,
    guestsCanModify: form.guestsCanModify,
    guestsCanInviteOthers: form.guestsCanInviteOthers,
    guestsCanSeeOtherGuests: form.guestsCanSeeOtherGuests,
  };
}

export function startOfWeek(date: Date) {
  const result = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  const mondayOffset = (result.getDay() + 6) % 7;
  result.setDate(result.getDate() - mondayOffset);
  return result;
}

export function addMonths(date: Date, amount: number) {
  const result = new Date(date);
  result.setDate(1);
  result.setMonth(result.getMonth() + amount);
  return result;
}

/** Local-day start as ISO (for occurredFrom/occurredTo query). */
export function localDayStartIso(date: Date): string {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate()).toISOString();
}

export function calendarQueryRange(cursor: Date, range: 'month' | 'week' | 'day' | 'year') {
  if (range === 'month') {
    const first = new Date(cursor.getFullYear(), cursor.getMonth(), 1);
    const offset = (first.getDay() + 6) % 7;
    const gridStart = addDays(first, -offset);
    const dayCount = Math.ceil((offset + new Date(cursor.getFullYear(), cursor.getMonth() + 1, 0).getDate()) / 7) * 7;
    const gridEnd = addDays(gridStart, dayCount);
    return { rangeStart: gridStart, rangeEnd: gridEnd };
  }
  if (range === 'week') {
    const weekStart = startOfWeek(cursor);
    return { rangeStart: weekStart, rangeEnd: addDays(weekStart, 7) };
  }
  if (range === 'day') {
    const start = new Date(cursor.getFullYear(), cursor.getMonth(), cursor.getDate());
    return { rangeStart: start, rangeEnd: addDays(start, 1) };
  }
  // year range
  const start = new Date(cursor.getFullYear(), 0, 1);
  return { rangeStart: start, rangeEnd: new Date(cursor.getFullYear() + 1, 0, 1) };
}
