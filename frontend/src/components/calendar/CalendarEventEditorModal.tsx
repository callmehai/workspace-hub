import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Bell,
  CalendarDays,
  ExternalLink,
  FileText,
  Folder,
  Loader2,
  Mail,
  MapPin,
  Paperclip,
  Plus,
  Trash2,
  Users,
  X,
} from 'lucide-react';
import { useQuery } from '@tanstack/react-query';
import type { ConnectionDto } from '../../lib/connectionsApi';
import { useI18n } from '../../hooks/useI18n';
import { Select } from '../Select';
import { itemsApi, foldersApi } from '../../lib/itemsApi';
import { GoogleDrivePickerModal } from '../drive/GoogleDrivePickerModal';
import { DriveIcon } from '../../lib/brandIcons';
import { driveItemOpenUrl, resolveGmailSuggestConnection } from '../../lib/calendarFormUtils';
import toast from 'react-hot-toast';
import { sendEmailApi } from '../../lib/sendEmailApi';
import { DatePicker } from '../DatePicker';
import { TimePicker } from '../TimePicker';
import type { ReminderType } from '../../types/items';

export interface CalendarEventFormValue {
  connectionId: string;
  title: string;
  date: string;
  endDate: string;
  allDay: boolean;
  startTime: string;
  endTime: string;
  location: string;
  attendees: string[];
  description: string;
  driveItemIds: string[];
  /** Snapshot từ Google sync / write-back — hiển thị khi chưa resolve được Item Drive. */
  driveAttachments: CalendarDriveAttachmentSnapshot[];
  reminders?: EventReminderFormValue[];
  recurrence?: string[];
  guestsCanModify: boolean;
  guestsCanInviteOthers: boolean;
  guestsCanSeeOtherGuests: boolean;
  folderIds?: string[];
}

export interface EventReminderFormValue {
  id?: string | null;
  reminderType: ReminderType;
  offsetValue: number;
  offsetUnit: 'Minutes' | 'Hours' | 'Days' | 'Weeks';
  timeOfDay?: string; // "HH:mm" e.g., "09:00"
}

export interface CalendarDriveAttachmentSnapshot {
  fileId: string;
  title?: string | null;
  mimeType?: string | null;
  fileUrl?: string | null;
}

type CustomRecurrenceFrequency = 'DAILY' | 'WEEKLY' | 'MONTHLY' | 'YEARLY';
type CustomRecurrenceEnd = 'never' | 'until' | 'count';

interface CustomRecurrenceValue {
  frequency: CustomRecurrenceFrequency;
  interval: number;
  weekDays: string[];
  endType: CustomRecurrenceEnd;
  until: string;
  count: number;
}

const RRULE_WEEK_DAYS = ['MO', 'TU', 'WE', 'TH', 'FR', 'SA', 'SU'] as const;
const GUEST_SUGGEST_DEBOUNCE_MS = 250;

function normalizeGuestEmail(raw: string) {
  return raw.trim().toLowerCase();
}

function splitGuestEmails(raw: string) {
  return raw.split(/[,;\s]+/).map(part => normalizeGuestEmail(part)).filter(Boolean);
}

function parseGuestDraft(attendees: string[], draft: string) {
  const next = [...attendees];
  const invalid: string[] = [];
  for (const email of splitGuestEmails(draft)) {
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
      invalid.push(email);
      continue;
    }
    if (!next.some(value => value.toLowerCase() === email)) next.push(email);
  }
  return { attendees: next, invalid };
}

function rruleDayForDate(date: string) {
  const [year, month, day] = date.split('-').map(Number);
  const value = new Date(year, month - 1, day).getDay();
  return ['SU', 'MO', 'TU', 'WE', 'TH', 'FR', 'SA'][value] || 'MO';
}

function defaultCustomRecurrence(date: string): CustomRecurrenceValue {
  return {
    frequency: 'WEEKLY',
    interval: 1,
    weekDays: [rruleDayForDate(date)],
    endType: 'never',
    until: date,
    count: 10,
  };
}

function parseCustomRecurrence(recurrence: string[] | undefined, date: string): CustomRecurrenceValue {
  const fallback = defaultCustomRecurrence(date);
  const raw = recurrence?.find(value => value.startsWith('RRULE:'))?.slice(6);
  if (!raw) return fallback;

  const fields = Object.fromEntries(raw.split(';').map(part => {
    const [key, ...value] = part.split('=');
    return [key, value.join('=')];
  }));
  const frequency = ['DAILY', 'WEEKLY', 'MONTHLY', 'YEARLY'].includes(fields.FREQ)
    ? fields.FREQ as CustomRecurrenceFrequency
    : fallback.frequency;
  const untilRaw = fields.UNTIL?.slice(0, 8);
  const until = untilRaw?.length === 8
    ? `${untilRaw.slice(0, 4)}-${untilRaw.slice(4, 6)}-${untilRaw.slice(6, 8)}`
    : date;

  return {
    frequency,
    interval: Math.max(1, Number.parseInt(fields.INTERVAL || '1', 10) || 1),
    weekDays: fields.BYDAY?.split(',').filter(Boolean) || [rruleDayForDate(date)],
    endType: fields.COUNT ? 'count' : fields.UNTIL ? 'until' : 'never',
    until,
    count: Math.max(1, Number.parseInt(fields.COUNT || '10', 10) || 10),
  };
}

function buildCustomRecurrence(value: CustomRecurrenceValue) {
  const parts = [`FREQ=${value.frequency}`, `INTERVAL=${Math.max(1, value.interval)}`];
  if (value.frequency === 'WEEKLY' && value.weekDays.length > 0) {
    parts.push(`BYDAY=${RRULE_WEEK_DAYS.filter(day => value.weekDays.includes(day)).join(',')}`);
  }
  if (value.endType === 'until' && value.until) {
    parts.push(`UNTIL=${value.until.replaceAll('-', '')}T235959Z`);
  }
  if (value.endType === 'count') {
    parts.push(`COUNT=${Math.max(1, value.count)}`);
  }
  return [`RRULE:${parts.join(';')}`];
}

interface CalendarEventEditorModalProps {
  open: boolean;
  mode: 'create' | 'edit';
  initialValue: CalendarEventFormValue;
  connections: ConnectionDto[];
  /** Toàn bộ connections user — dùng resolve Gmail suggest. Nếu thiếu, suggest tắt. */
  allConnections?: ConnectionDto[];
  folderName?: string | null;
  saving?: boolean;
  htmlLink?: string;
  onDelete?: () => void;
  canInviteOthers?: boolean;
  canManageGuestPermissions?: boolean;
  onClose: () => void;
  onSubmit: (value: CalendarEventFormValue) => void;
}

export function CalendarEventEditorModal({
  open,
  mode,
  initialValue,
  connections,
  allConnections,
  folderName,
  saving = false,
  htmlLink,
  onDelete,
  canInviteOthers = true,
  canManageGuestPermissions = true,
  onClose,
  onSubmit,
}: CalendarEventEditorModalProps) {
  const { t } = useI18n();
  const [form, setForm] = useState(initialValue);
  const [error, setError] = useState('');
  const [drivePickerOpen, setDrivePickerOpen] = useState(false);
  /** Một overlay mở tại một thời điểm (date/time picker + Select trong modal). */
  const [openOverlay, setOpenOverlay] = useState<string | null>(null);

  const bindOverlay = (id: string) => ({
    open: openOverlay === id,
    onOpenChange: (next: boolean) => setOpenOverlay(next ? id : null),
  });
  const [customRecurrenceOpen, setCustomRecurrenceOpen] = useState(false);
  const [guestDraft, setGuestDraft] = useState('');
  const [debouncedGuestQuery, setDebouncedGuestQuery] = useState('');
  const [guestSuggestOpen, setGuestSuggestOpen] = useState(false);
  const [customRecurrence, setCustomRecurrence] = useState<CustomRecurrenceValue>(
    () => parseCustomRecurrence(initialValue.recurrence, initialValue.date),
  );

  const { data: folders = [] } = useQuery({
    queryKey: ['folders'],
    queryFn: () => foldersApi.getFolders(false),
  });

  const [isAddingToFolder, setIsAddingToFolder] = useState(false);
  const [folderSearch, setFolderSearch] = useState('');
  const addFolderRef = useRef<HTMLDivElement>(null);

  const closeFolderPicker = () => {
    setIsAddingToFolder(false);
    setFolderSearch('');
  };

  useEffect(() => {
    if (!isAddingToFolder) return;
    const onDocClick = (e: MouseEvent) => {
      if (addFolderRef.current && !addFolderRef.current.contains(e.target as Node)) {
        closeFolderPicker();
      }
    };
    document.addEventListener('mousedown', onDocClick, true);
    return () => document.removeEventListener('mousedown', onDocClick, true);
  }, [isAddingToFolder]);

  const recurrenceOptions = useMemo(() => {
    if (!form.date) return [];
    const dateParts = form.date.split('-').map(Number);
    const dateObj = new Date(dateParts[0], dateParts[1] - 1, dateParts[2]);
    if (isNaN(dateObj.getTime())) return [];

    const dayOfWeek = dateObj.getDay();
    const dayOfMonth = dateObj.getDate();
    const month = dateObj.getMonth();
    
    const weekdayKeys = ['calendar.weekdaySun', 'calendar.weekdayMon', 'calendar.weekdayTue', 'calendar.weekdayWed', 'calendar.weekdayThu', 'calendar.weekdayFri', 'calendar.weekdaySat'] as const;
    const rruleDays = ['SU', 'MO', 'TU', 'WE', 'TH', 'FR', 'SA'];
    const dayName = t(weekdayKeys[dayOfWeek]);
    const rruleDay = rruleDays[dayOfWeek];

    return [
      {
        id: 'none',
        label: t('calendar.repeatNone'),
        rrule: [],
      },
      {
        id: 'daily',
        label: t('calendar.repeatDaily'),
        rrule: ['RRULE:FREQ=DAILY'],
      },
      {
        id: 'weekly',
        label: t('calendar.repeatWeeklyOn', { day: dayName }),
        rrule: [`RRULE:FREQ=WEEKLY;BYDAY=${rruleDay}`],
      },
      {
        id: 'monthly',
        label: t('calendar.repeatMonthlyOn', { day: dayOfMonth }),
        rrule: [`RRULE:FREQ=MONTHLY;BYMONTHDAY=${dayOfMonth}`],
      },
      {
        id: 'annually',
        label: t('calendar.repeatYearlyOn', { day: dayOfMonth, month: month + 1 }),
        rrule: ['RRULE:FREQ=YEARLY'],
      },
      {
        id: 'weekday',
        label: t('calendar.repeatWeekdays'),
        rrule: ['RRULE:FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR'],
      },
      {
        id: 'custom',
        label: t('calendar.repeatCustom'),
        rrule: null,
      },
    ];
  }, [form.date, t]);

  const selectedRecurrenceOption = useMemo(() => {
    const currentRrule = form.recurrence?.[0] || '';
    if (!currentRrule) return 'none';
    const matched = recurrenceOptions.find(opt => opt.rrule?.[0] === currentRrule);
    return matched ? matched.id : 'custom';
  }, [form.recurrence, recurrenceOptions]);

  const handleRecurrenceChange = (optionId: string) => {
    if (optionId === 'custom') {
      setCustomRecurrence(parseCustomRecurrence(form.recurrence, form.date));
      setOpenOverlay(null);
      setCustomRecurrenceOpen(true);
      return;
    }
    const option = recurrenceOptions.find(opt => opt.id === optionId);
    setForm(curr => ({
      ...curr,
      recurrence: option?.rrule || [],
    }));
  };

  // Reset form khi modal mở lại (parent có thể giữ cùng key, ví dụ ItemDetail).
  const [prevOpen, setPrevOpen] = useState(open);
  if (open !== prevOpen) {
    setPrevOpen(open);
    if (open) {
      setForm(initialValue);
      setOpenOverlay(null);
      setCustomRecurrence(parseCustomRecurrence(initialValue.recurrence, initialValue.date));
      setCustomRecurrenceOpen(false);
      setGuestDraft('');
      setGuestSuggestOpen(false);
    }
  }

  const suggestConnectionId = useMemo(
    () => (allConnections ? resolveGmailSuggestConnection(allConnections, form.connectionId) : undefined),
    [allConnections, form.connectionId],
  );

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedGuestQuery(guestDraft.trim()), GUEST_SUGGEST_DEBOUNCE_MS);
    return () => window.clearTimeout(timer);
  }, [guestDraft]);

  const guestSuggestEnabled = canInviteOthers && !!suggestConnectionId && debouncedGuestQuery.length >= 2;
  const { data: guestSuggestions = [] } = useQuery({
    queryKey: ['calendar-guest-suggest', suggestConnectionId, debouncedGuestQuery],
    queryFn: () => sendEmailApi.suggestContacts(suggestConnectionId!, debouncedGuestQuery),
    enabled: open && guestSuggestEnabled,
    staleTime: 60_000,
    retry: false,
  });

  const filteredGuestSuggestions = guestSuggestions.filter(suggestion =>
    !form.attendees.some(email => email.toLowerCase() === suggestion.email.toLowerCase()),
  );

  // Hiển thị chip: fetch đúng Item theo driveItemIds (không phụ thuộc top 100).
  const { data: attachedDriveItems } = useQuery({
    queryKey: ['calendar-attached-drive', form.driveItemIds],
    queryFn: async () => {
      const results = await Promise.all(
        form.driveItemIds.map(id => itemsApi.getItemById(id).catch(() => null)),
      );
      return results.filter((item): item is NonNullable<typeof item> => item != null);
    },
    enabled: open && form.driveItemIds.length > 0,
  });

  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !saving) onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, saving, onClose]);

  if (!open) return null;

  const selectedConnection = connections.find(connection => connection.id === form.connectionId);
  const organizerEmail = selectedConnection?.providerAccountId;

  const addGuest = (raw: string) => {
    const { attendees, invalid } = parseGuestDraft(form.attendees, raw);
    if (invalid.length > 0) {
      toast.error(t('calendar.invalidGuestEmail', { email: invalid[0] }));
      return;
    }
    if (attendees.length !== form.attendees.length) {
      setForm(current => ({ ...current, attendees }));
    }
    setGuestDraft('');
    setGuestSuggestOpen(false);
  };

  const removeGuest = (email: string) => {
    setForm(current => ({
      ...current,
      attendees: current.attendees.filter(value => value.toLowerCase() !== email.toLowerCase()),
    }));
  };

  const submit = () => {
    const title = form.title.trim();
    const endDate = form.endDate || form.date;
    
    if (!form.connectionId) {
      setError(t('calendar.errSelectAccount'));
      return;
    }
    if (!title) {
      setError(t('calendar.errTitle'));
      return;
    }
    if (!form.date) {
      setError(t('calendar.errStartDate'));
      return;
    }
    if (!endDate) {
      setError(t('calendar.errEndDate'));
      return;
    }
    if (!form.allDay) {
      if (!form.startTime) {
        setError(t('calendar.errStartTime'));
        return;
      }
      if (!form.endTime) {
        setError(t('calendar.errEndTime'));
        return;
      }
    }
    
    if (endDate < form.date || (!form.allDay && endDate === form.date && form.endTime <= form.startTime)) {
      setError(t('calendar.timeOrder'));
      return;
    }

    setError('');
    setGuestSuggestOpen(false);
    onSubmit({
      ...form,
      title,
      reminders: (form.reminders || []).map(reminder => {
        if (!form.allDay) {
          return { ...reminder, timeOfDay: undefined };
        }
        const offsetUnit = reminder.offsetUnit === 'Weeks' ? 'Weeks' : 'Days';
        return {
          ...reminder,
          offsetUnit,
          timeOfDay: reminder.timeOfDay || '09:00',
        };
      }),
    });
  };

  const addReminder = () => {
    const reminders = form.reminders || [];
    if (reminders.length >= 5) {
      toast.error(t('calendar.maxReminders'));
      return;
    }
    setForm(current => ({
      ...current,
      reminders: [
        ...(current.reminders || []),
        current.allDay
          ? { reminderType: 'GooglePopup', offsetValue: 1, offsetUnit: 'Days', timeOfDay: '09:00' }
          : { reminderType: 'GooglePopup', offsetValue: 15, offsetUnit: 'Minutes' },
      ],
    }));
  };

  const updateReminder = (index: number, patch: Partial<EventReminderFormValue>) => {
    setForm(current => ({
      ...current,
      reminders: (current.reminders || []).map((reminder, reminderIndex) => (
        reminderIndex === index ? { ...reminder, ...patch } : reminder
      )),
    }));
  };

  const removeReminder = (index: number) => {
    setForm(current => ({
      ...current,
      reminders: (current.reminders || []).filter((_, reminderIndex) => reminderIndex !== index),
    }));
  };

  const applyCustomRecurrence = () => {
    if (customRecurrence.frequency === 'WEEKLY' && customRecurrence.weekDays.length === 0) {
      toast.error(t('calendar.selectWeekday'));
      return;
    }
    if (customRecurrence.endType === 'until' && customRecurrence.until < form.date) {
      toast.error(t('calendar.repeatEndBeforeStart'));
      return;
    }
    setForm(current => ({ ...current, recurrence: buildCustomRecurrence(customRecurrence) }));
    setCustomRecurrenceOpen(false);
  };

  const baseInputClass = 'w-full rounded-lg border border-slate-200 bg-slate-50 px-3 text-[13px] text-slate-900 outline-none transition focus:border-brand-500 focus:bg-white focus:ring-2 focus:ring-brand-500/15 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100 dark:focus:bg-slate-850';
  const inputClass = `${baseInputClass} h-9`;
  const textareaClass = `${baseInputClass} min-h-36 py-3 resize-y hide-scrollbar`;
  const labelClass = 'mb-1.5 block text-[12px] font-semibold text-slate-500 dark:text-slate-400';
  const rowIconClass = 'mt-2.5 h-5 w-5 shrink-0 text-slate-400 dark:text-slate-500';

  return (
    <div
      className="fixed inset-0 z-[9000] flex items-center justify-center overflow-y-auto bg-slate-950/50 p-3 backdrop-blur-[2px] sm:p-5"
      onMouseDown={() => { if (!saving) onClose(); }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label={mode === 'create' ? t('calendar.createTitle') : t('calendar.editTitle')}
        className="flex h-[900px] max-h-[94vh] w-[72vw] max-w-[calc(100vw-1.5rem)] flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl dark:border-slate-700 dark:bg-slate-900"
        onMouseDown={event => event.stopPropagation()}
      >
        <header className="shrink-0 border-b border-slate-200 bg-white px-4 py-4 dark:border-slate-800 dark:bg-slate-900 sm:px-6">
          <div className="flex items-center gap-3 sm:gap-4">
            <button
              type="button"
              onClick={onClose}
              disabled={saving}
              aria-label={t('common.cancel')}
              className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-slate-500 transition hover:bg-slate-100 hover:text-slate-800 disabled:opacity-50 dark:text-slate-400 dark:hover:bg-slate-800 dark:hover:text-slate-100"
            >
              <X className="h-5 w-5" />
            </button>

            <input
              value={form.title}
              autoFocus
              onChange={event => setForm(current => ({ ...current, title: event.target.value }))}
              placeholder={t('calendar.addTitle')}
              className="min-w-0 flex-1 border-0 border-b-2 border-slate-300 bg-transparent px-0 py-2 text-[19px] font-semibold text-slate-900 outline-none transition placeholder:font-normal placeholder:text-slate-400 focus:border-brand-600 dark:border-slate-700 dark:text-slate-100 dark:focus:border-brand-400 sm:text-[22px]"
            />

            <div className="flex shrink-0 items-center gap-1.5 sm:gap-2">
              {mode === 'edit' && onDelete && (
                <button
                  type="button"
                  onClick={onDelete}
                  disabled={saving}
                  aria-label={t('common.delete')}
                  title={t('common.delete')}
                  className="flex h-10 w-10 items-center justify-center rounded-full text-slate-500 transition hover:bg-rose-50 hover:text-rose-600 disabled:opacity-50 dark:text-slate-400 dark:hover:bg-rose-500/10 dark:hover:text-rose-400"
                >
                  <Trash2 className="h-4.5 w-4.5" />
                </button>
              )}
              <button
                type="button"
                onClick={submit}
                disabled={saving || connections.length === 0}
                className="inline-flex h-10 items-center gap-1.5 rounded-full bg-brand-600 px-4 text-[13px] font-semibold text-white shadow-sm transition hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50 sm:px-5"
              >
                {saving && <Loader2 className="h-4 w-4 animate-spin" />}
                {t('common.save')}
              </button>
            </div>
          </div>

          <div className="ml-12 mt-4 space-y-3 sm:ml-13">
            <div className="flex flex-wrap items-center gap-2">
              <DatePicker
                variant="chip"
                value={form.date}
                onChange={date => setForm(current => ({
                  ...current,
                  date,
                  endDate: current.endDate < date ? date : current.endDate,
                }))}
                {...bindOverlay('date')}
              />
              {!form.allDay && (
                <TimePicker
                  variant="chip"
                  value={form.startTime}
                  onChange={startTime => setForm(current => ({ ...current, startTime }))}
                  {...bindOverlay('start')}
                />
              )}
              <span className="px-0.5 text-[13px] font-medium text-slate-600 dark:text-slate-300">
                {t('calendar.to')}
              </span>
              {!form.allDay && (
                <TimePicker
                  variant="chip"
                  value={form.endTime}
                  onChange={endTime => setForm(current => ({ ...current, endTime }))}
                  {...bindOverlay('end')}
                />
              )}
              <DatePicker
                variant="chip"
                value={form.endDate || form.date}
                onChange={endDate => setForm(current => ({
                  ...current,
                  endDate: endDate < current.date ? current.date : endDate,
                }))}
                {...bindOverlay('end-date')}
              />
            </div>

            <div className="flex flex-wrap items-center gap-3">
              <label className="inline-flex cursor-pointer items-center gap-2 text-[13px] font-medium text-slate-700 dark:text-slate-200">
              <input
                type="checkbox"
                checked={form.allDay}
                onChange={event => {
                  const allDay = event.target.checked;
                  setForm(current => ({
                    ...current,
                    allDay,
                    // Google all-day: chỉ ngày/tuần + "before at [time]". Timed: offset thuần, không giờ trong ngày.
                    reminders: (current.reminders || []).map(reminder => {
                      if (!allDay) {
                        return { ...reminder, timeOfDay: undefined };
                      }
                      const keepWeeks = reminder.offsetUnit === 'Weeks';
                      return {
                        ...reminder,
                        offsetUnit: keepWeeks ? 'Weeks' : 'Days',
                        offsetValue: (reminder.offsetUnit === 'Minutes' || reminder.offsetUnit === 'Hours')
                          ? 1
                          : reminder.offsetValue,
                        timeOfDay: reminder.timeOfDay || '09:00',
                      };
                    }),
                  }));
                  setOpenOverlay(null);
                }}
                  className="h-4 w-4 rounded border-slate-300 accent-brand-600 text-brand-600 focus:ring-brand-500"
              />
                <span>{t('calendar.allDay')}</span>
              </label>
              <div className="w-full sm:w-[320px]">
              <Select
                value={selectedRecurrenceOption}
                onChange={handleRecurrenceChange}
                options={recurrenceOptions.map(option => ({ value: option.id, label: option.label }))}
                className="h-9 bg-slate-50 text-[13px] dark:bg-slate-800"
                {...bindOverlay('recurrence')}
              />
              </div>
            </div>
          </div>
        </header>

        <div className="min-h-0 flex-1 overflow-y-auto bg-slate-50/70 p-4 dark:bg-slate-950/40 sm:p-6">
          <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_320px]">
            <section className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm dark:border-slate-800 dark:bg-slate-900">
              <div className="border-b border-slate-200 px-5 pt-4 dark:border-slate-800 sm:px-6">
                <div className="inline-flex border-b-2 border-brand-600 pb-3 text-[13px] font-semibold text-brand-700 dark:border-brand-400 dark:text-brand-300">
                  {t('calendar.eventDetails')}
                </div>
              </div>

              <div className="space-y-5 p-5 sm:p-6">
                <div className="flex gap-4">
                  <MapPin className={rowIconClass} />
                  <div className="min-w-0 flex-1">
                    <label className={labelClass}>{t('calendar.location')}</label>
                    <input
                      className={inputClass}
                      value={form.location}
                      placeholder={t('calendar.addLocation')}
                      onChange={event => setForm(current => ({ ...current, location: event.target.value }))}
                    />
                  </div>
                </div>

                <div className="flex gap-4">
                  <Bell className={rowIconClass} />
                  <div className="min-w-0 flex-1">
                    <label className="mb-2 block text-[12px] font-semibold text-slate-500 dark:text-slate-400">
                      {t('calendar.notifications')}
                    </label>

                    <div className="space-y-2">
                      {(!form.reminders || form.reminders.length === 0) && (
                        <p className="py-1 text-[12.5px] text-slate-400 dark:text-slate-500">
                          {t('calendar.noNotifications')}
                        </p>
                      )}
                      {(form.reminders || []).map((reminder, index) => {
                        // All-day Google: type · N · days|weeks · before at · time · ×
                        // Timed: type · N · minutes|hours|days|weeks · ×
                        const unitOptions = form.allDay
                          ? [
                              { value: 'Days', label: t('calendar.days') },
                              { value: 'Weeks', label: t('calendar.weeks') },
                            ]
                          : [
                              { value: 'Minutes', label: t('calendar.minutes') },
                              { value: 'Hours', label: t('calendar.hours') },
                              { value: 'Days', label: t('calendar.days') },
                              { value: 'Weeks', label: t('calendar.weeks') },
                            ];
                        return (
                          <div key={`${reminder.id ?? 'new'}-${index}`} className="flex flex-wrap items-center gap-2">
                            <div className="w-[250px] shrink-0 sm:w-[285px]">
                              <Select
                                value={reminder.reminderType ?? 'GooglePopup'}
                                onChange={value => updateReminder(index, { reminderType: value as ReminderType })}
                                options={[
                                  { value: 'InApp', label: t('calendar.reminderInApp') },
                                  { value: 'GooglePopup', label: t('calendar.reminderGoogle') },
                                  { value: 'GoogleEmail', label: 'Email' },
                                ]}
                                className="!h-9 !rounded-md !border-0 !bg-[#e8eaed] !px-2.5 text-[12.5px] !shadow-none hover:!bg-[#dde1e6] dark:!bg-slate-700 dark:hover:!bg-slate-600"
                                {...bindOverlay(`reminder-type-${index}`)}
                              />
                            </div>
                            <input
                              type="number"
                              min="1"
                              max="999"
                              value={reminder.offsetValue}
                              onChange={event => updateReminder(index, { offsetValue: Number.parseInt(event.target.value, 10) || 1 })}
                              aria-label={t('calendar.reminderLeadTime')}
                              className="h-9 w-[56px] shrink-0 rounded-md border-0 bg-[#e8eaed] px-2 text-center text-[13px] font-medium text-slate-800 outline-none transition hover:bg-[#dde1e6] focus:ring-2 focus:ring-brand-500/25 dark:bg-slate-700 dark:text-slate-100 dark:hover:bg-slate-600"
                            />
                            <div className="w-[96px] shrink-0">
                              <Select
                                value={reminder.offsetUnit}
                                onChange={value => {
                                  const offsetUnit = value as EventReminderFormValue['offsetUnit'];
                                  updateReminder(index, {
                                    offsetUnit,
                                    timeOfDay: form.allDay ? (reminder.timeOfDay || '09:00') : undefined,
                                  });
                                }}
                                options={unitOptions}
                                className="!h-9 !rounded-md !border-0 !bg-[#e8eaed] !px-2.5 text-[12.5px] !shadow-none hover:!bg-[#dde1e6] dark:!bg-slate-700 dark:hover:!bg-slate-600"
                                {...bindOverlay(`reminder-unit-${index}`)}
                              />
                            </div>
                            {form.allDay && (
                              <>
                                <span className="shrink-0 whitespace-nowrap text-[12.5px] text-slate-600 dark:text-slate-300">
                                  {t('calendar.beforeAt')}
                                </span>
                                <TimePicker
                                  variant="chip"
                                  value={reminder.timeOfDay || '09:00'}
                                  onChange={timeOfDay => updateReminder(index, { timeOfDay: timeOfDay || '09:00' })}
                                  {...bindOverlay(`reminder-time-${index}`)}
                                />
                              </>
                            )}
                            <button
                              type="button"
                              onClick={() => removeReminder(index)}
                              aria-label={t('calendar.removeNotification')}
                              className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-slate-400 transition hover:bg-slate-100 hover:text-slate-600 dark:hover:bg-slate-800 dark:hover:text-slate-200"
                            >
                              <X className="h-4 w-4" />
                            </button>
                          </div>
                        );
                      })}
                    </div>

                    <button
                      type="button"
                      onClick={addReminder}
                      className="mt-2 inline-flex items-center gap-1 text-[12.5px] font-semibold text-brand-600 transition hover:text-brand-700 dark:text-brand-400"
                    >
                      <Plus className="h-3.5 w-3.5" />
                      {t('calendar.addNotification')}
                    </button>
                  </div>
                </div>

                <div className="flex gap-4">
                  <CalendarDays className={rowIconClass} />
                  <div className="min-w-0 flex-1">
                    <label className={labelClass}>{t('calendar.account')}</label>
                    <Select
                      value={form.connectionId}
                      onChange={connectionId => setForm(current => ({ ...current, connectionId }))}
                      options={connections.map(connection => ({
                        value: connection.id,
                        label: `${connection.providerAccountId} · Google Calendar`,
                      }))}
                      placeholder={t('calendar.selectAccount')}
                      disabled={mode === 'edit'}
                      className="h-10 bg-slate-50 text-[13px] dark:bg-slate-800"
                      {...bindOverlay('connection')}
                    />
                    {connections.length === 0 && (
                      <p className="mt-1.5 text-[12px] text-amber-600 dark:text-amber-400">{t('calendar.noConnection')}</p>
                    )}
                  </div>
                </div>

                <div className="flex gap-4">
                  <Folder className={rowIconClass} />
                  <div className="min-w-0 flex-1">
                    <label className={labelClass}>{t('calendar.folders')}</label>
                    <div className="flex flex-wrap items-center gap-1.5 pt-1">
                      {form.folderIds?.map(fId => {
                        const f = folders.find(fol => fol.id === fId);
                        if (!f) return null;
                        return (
                          <span
                            key={f.id}
                            style={{ borderColor: f.color || '#94a3b8', color: f.color || '#64748b' }}
                            className="inline-flex items-center gap-1.5 rounded-full border bg-white dark:bg-slate-800 py-0.5 pl-2.5 pr-1 text-[11px] font-semibold shadow-sm transition hover:bg-slate-50 dark:hover:bg-slate-700"
                          >
                            <span className="w-1.5 h-1.5 rounded-full shrink-0" style={{ backgroundColor: f.color || '#94a3b8' }} />
                            <span className="truncate max-w-[120px]">{f.name}</span>
                            <button
                              type="button"
                              onClick={() => {
                                const nextIds = (form.folderIds ?? []).filter(id => id !== f.id);
                                setForm(curr => ({ ...curr, folderIds: nextIds }));
                              }}
                              className="p-0.5 rounded-full hover:bg-slate-100 dark:hover:bg-slate-700 hover:text-rose-600 transition"
                              title={t('calendar.removeFromFolder')}
                            >
                              <X className="w-3 h-3" />
                            </button>
                          </span>
                        );
                      })}

                      {/* Dropdown to add folder */}
                      <div className="relative" ref={addFolderRef}>
                        <button
                          type="button"
                          onClick={() => {
                            if (isAddingToFolder) closeFolderPicker();
                            else {
                              setFolderSearch('');
                              setIsAddingToFolder(true);
                            }
                          }}
                          className="inline-flex items-center justify-center gap-1 h-[24px] px-2.5 rounded-full bg-slate-100 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-200 dark:hover:bg-slate-700 hover:text-slate-800 dark:hover:text-white transition-colors text-[11px] font-semibold"
                          title={t('calendar.addToFolder')}
                        >
                          <Plus className="w-3 h-3" />
                          <span>{t('calendar.add')}</span>
                        </button>

                        {isAddingToFolder && (
                          <div className="absolute top-full left-0 mt-1.5 w-48 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 shadow-xl rounded-xl py-1.5 z-[9000] animate-in fade-in zoom-in-95 duration-100">
                            <div className="px-2 py-1.5 border-b border-slate-100 dark:border-slate-700">
                              <input
                                type="text"
                                placeholder={t('calendar.searchFolders')}
                                value={folderSearch}
                                onChange={e => setFolderSearch(e.target.value)}
                                className="w-full px-2 py-1 text-xs bg-slate-50 dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded outline-none focus:border-brand-500 focus:ring-1 focus:ring-brand-500/20 text-slate-900 dark:text-slate-100"
                              />
                            </div>
                            {folders.filter(f => !form.folderIds?.includes(f.id)).length === 0 ? (
                              <div className="px-3 py-2 text-xs text-slate-500 dark:text-slate-400 text-center">{t('calendar.alreadyInAllFolders')}</div>
                            ) : folders.filter(f => !form.folderIds?.includes(f.id) && f.name.toLowerCase().includes(folderSearch.toLowerCase())).length === 0 ? (
                              <div className="px-3 py-2 text-xs text-slate-500 dark:text-slate-400 text-center">{t('calendar.noFoldersFound')}</div>
                            ) : (
                              folders
                                .filter(f => !form.folderIds?.includes(f.id) && f.name.toLowerCase().includes(folderSearch.toLowerCase()))
                                .map(f => (
                                  <button
                                    key={f.id}
                                    type="button"
                                    onClick={() => {
                                      const nextIds = [...(form.folderIds ?? []), f.id];
                                      setForm(curr => ({ ...curr, folderIds: nextIds }));
                                      closeFolderPicker();
                                    }}
                                    className="w-full text-left px-3.5 py-2 text-xs font-semibold text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700 flex items-center gap-2.5 transition-colors"
                                  >
                                    <span className="w-2 h-2 rounded-full" style={{ backgroundColor: f.color || '#f59e0b' }}></span>
                                    <span className="truncate">{f.name}</span>
                                  </button>
                                ))
                            )}
                          </div>
                        )}
                      </div>
                    </div>
                  </div>
                </div>

                <div className="flex gap-4">
                  <Paperclip className={rowIconClass} />
                  <div className="min-w-0 flex-1">
                    <button
                      type="button"
                      disabled={!form.connectionId}
                      onClick={() => {
                        setOpenOverlay(null);
                        setDrivePickerOpen(true);
                      }}
                      className="inline-flex h-9 items-center gap-2 rounded-lg px-2 text-[13px] font-semibold text-brand-600 transition hover:bg-brand-50 hover:text-brand-700 disabled:text-slate-400 dark:text-brand-400 dark:hover:bg-brand-500/10"
                    >
                      <DriveIcon className="h-5 w-5 shrink-0" />
                      <span>{t('calendar.addDriveFile')}</span>
                    </button>

                    {(form.driveItemIds.length > 0 || form.driveAttachments.length > 0) && (
                      <div className="mt-2 flex flex-wrap gap-2">
                {form.driveItemIds.map(id => {
                  const file = attachedDriveItems?.find(f => f.id === id);
                  const snapshot = file?.externalId
                    ? form.driveAttachments.find(a => a.fileId === file.externalId)
                    : undefined;
                  const title = file?.title ?? snapshot?.title ?? id;
                  const fileUrl = snapshot?.fileUrl ?? (file ? driveItemOpenUrl(file) : null);
                  return (
                    <div
                      key={id}
                              className="inline-flex max-w-[280px] items-center gap-1.5 rounded-full border border-slate-200 bg-slate-50 py-1 pl-2.5 pr-1.5 text-[12.5px] dark:border-slate-700 dark:bg-slate-800"
                    >
                      <FileText className="w-3.5 h-3.5 text-brand-500 shrink-0" />
                      {fileUrl ? (
                        <a
                          href={fileUrl}
                          target="_blank"
                          rel="noreferrer"
                          className="truncate flex-1 text-slate-700 dark:text-slate-200 hover:underline"
                          title={title}
                          onClick={e => e.stopPropagation()}
                        >
                          {title}
                        </a>
                      ) : (
                        <span className="truncate flex-1 text-slate-700 dark:text-slate-200" title={title}>
                          {title}
                        </span>
                      )}
                      <button
                        type="button"
                        onClick={() => setForm(curr => ({
                          ...curr,
                          driveItemIds: curr.driveItemIds.filter(x => x !== id),
                          driveAttachments: file?.externalId
                            ? curr.driveAttachments.filter(a => a.fileId !== file.externalId)
                            : curr.driveAttachments,
                        }))}
                        className="p-0.5 rounded-full text-slate-400 hover:text-rose-500 hover:bg-slate-200 dark:hover:bg-slate-700 transition-colors"
                      >
                        <X className="w-3.5 h-3.5" />
                      </button>
                    </div>
                  );
                })}
                {form.driveAttachments
                  .filter(a => !form.driveItemIds.some(id => {
                    const file = attachedDriveItems?.find(f => f.id === id);
                    return file?.externalId === a.fileId;
                  }))
                  .map(a => (
                            <div key={a.fileId || a.fileUrl || a.title} className="inline-flex max-w-[280px] items-center gap-1.5 rounded-full border border-slate-200 bg-slate-50 py-1 pl-2.5 pr-2.5 text-[12.5px] dark:border-slate-700 dark:bg-slate-800">
                      <FileText className="w-3.5 h-3.5 text-brand-500 shrink-0" />
                      {a.fileUrl ? (
                        <a
                          href={a.fileUrl}
                          target="_blank"
                          rel="noreferrer"
                          className="truncate flex-1 text-slate-700 dark:text-slate-200 hover:underline"
                          title={a.title ?? a.fileId}
                          onClick={e => e.stopPropagation()}
                        >
                          {a.title ?? a.fileId}
                        </a>
                      ) : (
                        <span className="truncate flex-1 text-slate-700 dark:text-slate-200" title={a.title ?? a.fileId}>
                          {a.title ?? a.fileId}
                        </span>
                      )}
                    </div>
                  ))}
                      </div>
                    )}
                  </div>
                </div>

                <div className="flex gap-4">
                  <FileText className={rowIconClass} />
                  <div className="min-w-0 flex-1">
                    <label className={labelClass}>{t('calendar.description')}</label>
                    <textarea
                      className={textareaClass}
                      value={form.description}
                      placeholder={t('calendar.descriptionPlaceholder')}
                      onChange={event => setForm(current => ({ ...current, description: event.target.value }))}
                    />
                  </div>
                </div>

                {folderName && (
                  <div className="ml-9 rounded-lg bg-brand-50 px-3 py-2 text-[12px] text-brand-700 dark:bg-brand-500/10 dark:text-brand-300">
                    {t('calendar.folderAssign', { folder: folderName })}
                  </div>
                )}
              </div>
            </section>

            <aside className="self-start overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm dark:border-slate-800 dark:bg-slate-900 lg:sticky lg:top-0">
              <div className="border-b border-slate-200 px-5 pt-4 dark:border-slate-800">
                <div className="inline-flex items-center gap-2 border-b-2 border-brand-600 pb-3 text-[13px] font-semibold text-brand-700 dark:border-brand-400 dark:text-brand-300">
                  <Users className="h-4 w-4" />
                  {t('calendar.guests')}
                  {form.attendees.length > 0 && (
                    <span className="rounded-full bg-brand-50 px-2 py-0.5 text-[11px] dark:bg-brand-500/10">{form.attendees.length}</span>
                  )}
                </div>
              </div>
              <div className="space-y-4 p-5">
                <div>
                  {canInviteOthers ? (
                    <div className="relative">
                      <input
                        value={guestDraft}
                        onChange={event => {
                          setGuestDraft(event.target.value);
                          setOpenOverlay(null);
                          setGuestSuggestOpen(true);
                        }}
                        onFocus={() => {
                          setOpenOverlay(null);
                          setGuestSuggestOpen(true);
                        }}
                        onKeyDown={event => {
                          if (event.key === 'Enter' || event.key === ',' || event.key === ';' || event.key === 'Tab') {
                            if (guestDraft.trim()) {
                              event.preventDefault();
                              addGuest(guestDraft);
                            }
                          }
                          if (event.key === 'Escape') setGuestSuggestOpen(false);
                        }}
                        onPaste={event => {
                          const text = event.clipboardData.getData('text');
                          if (/[,;\s]/.test(text)) {
                            event.preventDefault();
                            addGuest(text);
                          }
                        }}
                        placeholder={t('calendar.addGuests')}
                        className="h-11 w-full rounded-t-lg border-0 border-b-2 border-brand-600 bg-slate-100 px-4 text-[14px] text-slate-900 outline-none transition placeholder:text-slate-500 focus:bg-slate-50 dark:bg-slate-800 dark:text-slate-100 dark:placeholder:text-slate-400 dark:focus:bg-slate-850"
                        autoComplete="off"
                      />
                      {guestDraft.trim() && (
                        <button
                          type="button"
                          onClick={() => addGuest(guestDraft)}
                          className="absolute right-2 top-2 flex h-7 w-7 items-center justify-center rounded-full text-brand-600 transition hover:bg-brand-50 dark:text-brand-300 dark:hover:bg-brand-500/10"
                          aria-label={t('calendar.addGuest')}
                        >
                          <Plus className="h-4 w-4" />
                        </button>
                      )}
                      {guestSuggestOpen && guestSuggestEnabled && filteredGuestSuggestions.length > 0 && (
                        <div className="absolute left-0 right-0 z-30 mt-1 max-h-56 overflow-y-auto rounded-lg border border-slate-200 bg-white py-1 shadow-lg dark:border-slate-700 dark:bg-slate-800">
                          {filteredGuestSuggestions.map(suggestion => (
                            <button
                              key={suggestion.email}
                              type="button"
                              onMouseDown={event => event.preventDefault()}
                              onClick={() => addGuest(suggestion.email)}
                              className="flex w-full items-center gap-3 px-3 py-2 text-left text-[13px] text-slate-800 transition hover:bg-slate-50 dark:text-slate-200 dark:hover:bg-slate-700/70"
                            >
                              <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-brand-600 text-[12px] font-semibold uppercase text-white">
                                {(suggestion.displayName || suggestion.email).slice(0, 1)}
                              </span>
                              <span className="min-w-0">
                                <span className="block truncate font-medium">{suggestion.displayName || suggestion.email}</span>
                                {suggestion.displayName && <span className="block truncate text-xs text-slate-500 dark:text-slate-400">{suggestion.email}</span>}
                              </span>
                            </button>
                          ))}
                        </div>
                      )}
                    </div>
                  ) : (
                    <div className="rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-[13px] text-slate-500 dark:border-slate-700 dark:bg-slate-800/60 dark:text-slate-400">
                      {t('calendar.guestsCannotInvite')}
                    </div>
                  )}
                </div>

                <div className="space-y-3">
                  <div className="flex items-center justify-between text-[13px]">
                    <div>
                      <div className="font-semibold text-slate-900 dark:text-slate-100">
                        {form.attendees.length} {t('calendar.guestCount')}
                      </div>
                      <div className="text-slate-500 dark:text-slate-400">
                        {form.attendees.length} {t('calendar.awaiting')}
                      </div>
                    </div>
                    {form.attendees.length > 0 && (
                      <Mail className="h-4.5 w-4.5 text-slate-500 dark:text-slate-400" />
                    )}
                  </div>

                  <div className="space-y-2">
                    {organizerEmail && (
                      <div className="flex items-center gap-3">
                        <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-brand-600 text-[12px] font-semibold uppercase text-white">
                          {organizerEmail.slice(0, 1)}
                        </span>
                        <div className="min-w-0">
                          <div className="truncate text-[13.5px] font-medium text-slate-900 dark:text-slate-100">
                            {organizerEmail}
                          </div>
                          <div className="text-xs text-slate-500 dark:text-slate-400">
                            {t('calendar.organizer')}
                          </div>
                        </div>
                      </div>
                    )}
                    {form.attendees.map(email => (
                      <div key={email} className="group flex items-center gap-3 rounded-lg py-1.5">
                        <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-slate-200 text-[12px] font-semibold uppercase text-slate-700 dark:bg-slate-700 dark:text-slate-100">
                          {email.slice(0, 1)}
                        </span>
                        <div className="min-w-0 flex-1">
                          <div className="truncate text-[13.5px] font-medium text-slate-900 dark:text-slate-100">{email}</div>
                        </div>
                        {canInviteOthers && (
                          <button
                            type="button"
                            onClick={() => removeGuest(email)}
                            className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-slate-400 opacity-100 transition hover:bg-rose-50 hover:text-rose-600 dark:hover:bg-rose-500/10 dark:hover:text-rose-300 sm:opacity-0 sm:group-hover:opacity-100"
                            aria-label={t('calendar.removeGuest', { email })}
                          >
                            <X className="h-4 w-4" />
                          </button>
                        )}
                      </div>
                    ))}
                  </div>

                </div>

                {canManageGuestPermissions && <div className="border-t border-slate-100 pt-4 dark:border-slate-800">
                  <h4 className="mb-2.5 text-[12px] font-semibold text-slate-700 dark:text-slate-200">
                    {t('calendar.guestPermissions')}
                  </h4>
                  <div className="space-y-1">
                    {[
                      { key: 'guestsCanModify' as const, labelKey: 'calendar.guestCanModify' as const },
                      { key: 'guestsCanInviteOthers' as const, labelKey: 'calendar.guestCanInvite' as const },
                      { key: 'guestsCanSeeOtherGuests' as const, labelKey: 'calendar.guestCanSeeOthers' as const },
                    ].map(permission => (
                      <label
                        key={permission.key}
                        className="flex cursor-pointer items-center gap-3 rounded-lg px-2 py-2 text-[13px] text-slate-700 transition hover:bg-slate-50 dark:text-slate-200 dark:hover:bg-slate-800/70"
                      >
                        <input
                          type="checkbox"
                          checked={form[permission.key]}
                          onChange={event => setForm(current => ({
                            ...current,
                            [permission.key]: event.target.checked,
                          }))}
                          className="h-4 w-4 rounded border-slate-300 accent-brand-600 text-brand-600 focus:ring-brand-500"
                        />
                        <span>{t(permission.labelKey)}</span>
                      </label>
                    ))}
                  </div>
                </div>}

                {mode === 'edit' && htmlLink && (
                  <a
                    href={htmlLink}
                    target="_blank"
                    rel="noreferrer"
                    className="inline-flex h-9 w-full items-center justify-center gap-1.5 rounded-lg border border-slate-200 text-[12.5px] font-semibold text-slate-600 transition hover:bg-slate-50 dark:border-slate-700 dark:text-slate-300 dark:hover:bg-slate-800 sm:hidden"
                  >
                    <ExternalLink className="h-4 w-4" />
                    Google Calendar
                  </a>
                )}
              </div>
            </aside>
          </div>

          {error && (
            <div className="mt-4 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-[12.5px] text-rose-700 dark:border-rose-500/20 dark:bg-rose-500/10 dark:text-rose-300">
              {error}
            </div>
          )}
        </div>
      </div>

      {customRecurrenceOpen && (
        <div
          className="fixed inset-0 z-[9200] flex items-center justify-center bg-slate-950/35 p-4"
          onMouseDown={() => setCustomRecurrenceOpen(false)}
        >
          <div
            role="dialog"
            aria-modal="true"
            aria-label={t('calendar.customRecurrence')}
            className="w-full max-w-md overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl dark:border-slate-700 dark:bg-slate-900"
            onMouseDown={event => event.stopPropagation()}
          >
            <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4 dark:border-slate-800">
              <h3 className="text-[16px] font-semibold text-slate-900 dark:text-slate-100">
                {t('calendar.customRecurrence')}
              </h3>
              <button
                type="button"
                onClick={() => setCustomRecurrenceOpen(false)}
                className="flex h-8 w-8 items-center justify-center rounded-full text-slate-400 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-slate-800 dark:hover:text-slate-200"
              >
                <X className="h-4 w-4" />
              </button>
            </div>

            <div className="space-y-5 p-5">
              <div>
                <label className={labelClass}>{t('calendar.repeatEvery')}</label>
                <div className="flex gap-2">
                  <input
                    type="number"
                    min="1"
                    max="99"
                    value={customRecurrence.interval}
                    onChange={event => setCustomRecurrence(current => ({
                      ...current,
                      interval: Math.max(1, Number.parseInt(event.target.value, 10) || 1),
                    }))}
                    className="h-10 w-20 rounded-lg border border-slate-200 bg-slate-50 px-3 text-[13px] text-slate-900 outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/15 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100"
                  />
                  <div className="min-w-0 flex-1">
                  <Select
                    value={customRecurrence.frequency}
                    onChange={value => setCustomRecurrence(current => ({
                      ...current,
                      frequency: value as CustomRecurrenceFrequency,
                    }))}
                    options={[
                      { value: 'DAILY', label: t('calendar.unitDay') },
                      { value: 'WEEKLY', label: t('calendar.unitWeek') },
                      { value: 'MONTHLY', label: t('calendar.unitMonth') },
                      { value: 'YEARLY', label: t('calendar.unitYear') },
                    ]}
                    className="h-10 bg-slate-50 text-[13px] dark:bg-slate-800"
                    {...bindOverlay('custom-freq')}
                  />
                  </div>
                </div>
              </div>

              {customRecurrence.frequency === 'WEEKLY' && (
                <div>
                  <label className={labelClass}>{t('calendar.repeatOn')}</label>
                  <div className="flex flex-wrap gap-2">
                    {RRULE_WEEK_DAYS.map((day, index) => {
                      const labels = [t('calendar.dowMon'), t('calendar.dowTue'), t('calendar.dowWed'), t('calendar.dowThu'), t('calendar.dowFri'), t('calendar.dowSat'), t('calendar.dowSun')];
                      const selected = customRecurrence.weekDays.includes(day);
                      return (
                        <button
                          type="button"
                          key={day}
                          onClick={() => setCustomRecurrence(current => ({
                            ...current,
                            weekDays: selected
                              ? current.weekDays.filter(value => value !== day)
                              : [...current.weekDays, day],
                          }))}
                          className={`flex h-9 min-w-9 items-center justify-center rounded-full px-2 text-[12px] font-semibold transition ${selected
                            ? 'bg-brand-600 text-white'
                            : 'bg-slate-100 text-slate-600 hover:bg-slate-200 dark:bg-slate-800 dark:text-slate-300 dark:hover:bg-slate-700'}`}
                        >
                          {labels[index]}
                        </button>
                      );
                    })}
                  </div>
                </div>
              )}

              <div>
                <label className={labelClass}>{t('calendar.ends')}</label>
                <div className="space-y-3 text-[13px] text-slate-700 dark:text-slate-200">
                  <label className="flex cursor-pointer items-center gap-3">
                    <input
                      type="radio"
                      name="recurrence-end"
                      checked={customRecurrence.endType === 'never'}
                      onChange={() => setCustomRecurrence(current => ({ ...current, endType: 'never' }))}
                      className="h-4 w-4 accent-brand-600 text-brand-600 focus:ring-brand-500"
                    />
                    {t('calendar.never')}
                  </label>
                  <label className="flex cursor-pointer flex-wrap items-center gap-3">
                    <input
                      type="radio"
                      name="recurrence-end"
                      checked={customRecurrence.endType === 'until'}
                      onChange={() => setCustomRecurrence(current => ({ ...current, endType: 'until' }))}
                      className="h-4 w-4 accent-brand-600 text-brand-600 focus:ring-brand-500"
                    />
                    <span className="min-w-16">{t('calendar.onDate')}</span>
                    <input
                      type="date"
                      min={form.date}
                      value={customRecurrence.until}
                      disabled={customRecurrence.endType !== 'until'}
                      onChange={event => setCustomRecurrence(current => ({ ...current, until: event.target.value }))}
                      className="h-9 rounded-lg border border-slate-200 bg-slate-50 px-2.5 text-[12.5px] outline-none disabled:opacity-50 dark:border-slate-700 dark:bg-slate-800"
                    />
                  </label>
                  <label className="flex cursor-pointer flex-wrap items-center gap-3">
                    <input
                      type="radio"
                      name="recurrence-end"
                      checked={customRecurrence.endType === 'count'}
                      onChange={() => setCustomRecurrence(current => ({ ...current, endType: 'count' }))}
                      className="h-4 w-4 accent-brand-600 text-brand-600 focus:ring-brand-500"
                    />
                    <span className="min-w-16">{t('calendar.after')}</span>
                    <input
                      type="number"
                      min="1"
                      max="999"
                      value={customRecurrence.count}
                      disabled={customRecurrence.endType !== 'count'}
                      onChange={event => setCustomRecurrence(current => ({
                        ...current,
                        count: Math.max(1, Number.parseInt(event.target.value, 10) || 1),
                      }))}
                      className="h-9 w-20 rounded-lg border border-slate-200 bg-slate-50 px-2.5 text-center text-[12.5px] outline-none disabled:opacity-50 dark:border-slate-700 dark:bg-slate-800"
                    />
                    <span>{t('calendar.occurrences')}</span>
                  </label>
                </div>
              </div>
            </div>

            <div className="flex justify-end gap-2 border-t border-slate-200 bg-slate-50 px-5 py-3.5 dark:border-slate-800 dark:bg-slate-800/50">
              <button
                type="button"
                onClick={() => setCustomRecurrenceOpen(false)}
                className="h-9 rounded-lg px-4 text-[13px] font-semibold text-slate-600 hover:bg-slate-200 dark:text-slate-300 dark:hover:bg-slate-700"
              >
                {t('common.cancel')}
              </button>
              <button
                type="button"
                onClick={applyCustomRecurrence}
                className="h-9 rounded-lg bg-brand-600 px-4 text-[13px] font-semibold text-white hover:bg-brand-700"
              >
                {t('common.save')}
              </button>
            </div>
          </div>
        </div>
      )}
      
      <GoogleDrivePickerModal
        open={drivePickerOpen}
        connectionId={form.connectionId}
        initialSelectedIds={form.driveItemIds}
        onClose={() => setDrivePickerOpen(false)}
        onSelect={selectedIds => setForm(curr => ({ ...curr, driveItemIds: selectedIds }))}
      />
    </div>
  );
}

