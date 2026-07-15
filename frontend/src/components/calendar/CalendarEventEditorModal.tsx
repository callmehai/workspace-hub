import { useEffect, useMemo, useState } from 'react';
import {
  Bell,
  CalendarDays,
  ExternalLink,
  FileText,
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
import { itemsApi } from '../../lib/itemsApi';
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
  const { t, lang } = useI18n();
  const [form, setForm] = useState(initialValue);
  const [error, setError] = useState('');
  const [drivePickerOpen, setDrivePickerOpen] = useState(false);
  const [openPicker, setOpenPicker] = useState<'date' | 'start' | 'end' | 'end-date' | null>(null);
  const [customRecurrenceOpen, setCustomRecurrenceOpen] = useState(false);
  const [guestDraft, setGuestDraft] = useState('');
  const [debouncedGuestQuery, setDebouncedGuestQuery] = useState('');
  const [guestSuggestOpen, setGuestSuggestOpen] = useState(false);
  const [customRecurrence, setCustomRecurrence] = useState<CustomRecurrenceValue>(
    () => parseCustomRecurrence(initialValue.recurrence, initialValue.date),
  );

  const recurrenceOptions = useMemo(() => {
    if (!form.date) return [];
    const dateParts = form.date.split('-').map(Number);
    const dateObj = new Date(dateParts[0], dateParts[1] - 1, dateParts[2]);
    if (isNaN(dateObj.getTime())) return [];

    const dayOfWeek = dateObj.getDay();
    const dayOfMonth = dateObj.getDate();
    const month = dateObj.getMonth();
    
    const dayNamesVi = ['Chủ Nhật', 'thứ Hai', 'thứ Ba', 'thứ Tư', 'thứ Năm', 'thứ Sáu', 'thứ Bảy'];
    const dayNamesEn = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
    const rruleDays = ['SU', 'MO', 'TU', 'WE', 'TH', 'FR', 'SA'];
    
    const dayNameVi = dayNamesVi[dayOfWeek];
    const dayNameEn = dayNamesEn[dayOfWeek];
    const rruleDay = rruleDays[dayOfWeek];
    const monthNamesEn = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

    return [
      {
        id: 'none',
        label: lang === 'vi' ? 'Không lặp lại' : 'Do not repeat',
        rrule: [],
      },
      {
        id: 'daily',
        label: lang === 'vi' ? 'Hàng ngày' : 'Daily',
        rrule: ['RRULE:FREQ=DAILY'],
      },
      {
        id: 'weekly',
        label: lang === 'vi' ? `Hàng tuần vào ${dayNameVi}` : `Weekly on ${dayNameEn}`,
        rrule: [`RRULE:FREQ=WEEKLY;BYDAY=${rruleDay}`],
      },
      {
        id: 'monthly',
        label: lang === 'vi' ? `Hàng tháng vào ngày ${dayOfMonth}` : `Monthly on day ${dayOfMonth}`,
        rrule: [`RRULE:FREQ=MONTHLY;BYMONTHDAY=${dayOfMonth}`],
      },
      {
        id: 'annually',
        label: lang === 'vi' ? `Hàng năm vào ngày ${dayOfMonth} tháng ${month + 1}` : `Annually on ${monthNamesEn[month]} ${dayOfMonth}`,
        rrule: ['RRULE:FREQ=YEARLY'],
      },
      {
        id: 'weekday',
        label: lang === 'vi' ? 'Mọi ngày trong tuần (từ thứ Hai tới thứ Sáu)' : 'Every weekday (Monday to Friday)',
        rrule: ['RRULE:FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR'],
      },
      {
        id: 'custom',
        label: lang === 'vi' ? 'Tùy chỉnh…' : 'Custom…',
        rrule: null,
      },
    ];
  }, [form.date, lang]);

  const selectedRecurrenceOption = useMemo(() => {
    const currentRrule = form.recurrence?.[0] || '';
    if (!currentRrule) return 'none';
    const matched = recurrenceOptions.find(opt => opt.rrule?.[0] === currentRrule);
    return matched ? matched.id : 'custom';
  }, [form.recurrence, recurrenceOptions]);

  const handleRecurrenceChange = (optionId: string) => {
    if (optionId === 'custom') {
      setCustomRecurrence(parseCustomRecurrence(form.recurrence, form.date));
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
      setOpenPicker(null);
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
      setError(lang === 'vi' ? 'Vui lòng chọn tài khoản Google Calendar.' : 'Please select a Google Calendar account.');
      return;
    }
    if (!title) {
      setError(lang === 'vi' ? 'Vui lòng nhập tiêu đề sự kiện.' : 'Please enter the event title.');
      return;
    }
    if (!form.date) {
      setError(lang === 'vi' ? 'Vui lòng chọn ngày bắt đầu.' : 'Please select a start date.');
      return;
    }
    if (!endDate) {
      setError(lang === 'vi' ? 'Vui lòng chọn ngày kết thúc.' : 'Please select an end date.');
      return;
    }
    if (!form.allDay) {
      if (!form.startTime) {
        setError(lang === 'vi' ? 'Vui lòng chọn giờ bắt đầu.' : 'Please select a start time.');
        return;
      }
      if (!form.endTime) {
        setError(lang === 'vi' ? 'Vui lòng chọn giờ kết thúc.' : 'Please select an end time.');
        return;
      }
    }
    
    if (endDate < form.date || (!form.allDay && endDate === form.date && form.endTime <= form.startTime)) {
      setError(t('calendar.timeOrder'));
      return;
    }

    setError('');
    setGuestSuggestOpen(false);
    onSubmit({ ...form, title });
  };

  const addReminder = () => {
    const reminders = form.reminders || [];
    if (reminders.length >= 5) {
      toast.error(lang === 'vi' ? 'Tối đa 5 nhắc nhở' : 'Maximum 5 reminders');
      return;
    }
    setForm(current => ({
      ...current,
      reminders: [
        ...(current.reminders || []),
        { reminderType: 'GooglePopup', offsetValue: 15, offsetUnit: 'Minutes' },
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
      toast.error(lang === 'vi' ? 'Chọn ít nhất một ngày trong tuần' : 'Select at least one weekday');
      return;
    }
    if (customRecurrence.endType === 'until' && customRecurrence.until < form.date) {
      toast.error(lang === 'vi' ? 'Ngày kết thúc lặp phải từ ngày bắt đầu' : 'Repeat end date must follow the start date');
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
        className="flex h-[900px] max-h-[94vh] w-[1240px] max-w-[calc(100vw-24px)] flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl dark:border-slate-700 dark:bg-slate-900"
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
              placeholder={lang === 'vi' ? 'Thêm tiêu đề' : 'Add title'}
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
            <div className="flex flex-wrap items-end gap-2.5">
              <div className="min-w-[136px] flex-1 sm:max-w-[170px]">
                <label className={labelClass}>{lang === 'vi' ? 'Bắt đầu' : 'Start'}</label>
                <DatePicker
                  value={form.date}
                  onChange={date => setForm(current => ({
                    ...current,
                    date,
                    endDate: current.endDate < date ? date : current.endDate,
                  }))}
                  open={openPicker === 'date'}
                  onOpenChange={next => setOpenPicker(next ? 'date' : null)}
                />
              </div>
              {!form.allDay && (
                <div className="w-[106px]">
                  <label className={labelClass}>{t('calendar.start')}</label>
                  <TimePicker
                    value={form.startTime}
                    onChange={startTime => setForm(current => ({ ...current, startTime }))}
                    open={openPicker === 'start'}
                    onOpenChange={next => setOpenPicker(next ? 'start' : null)}
                  />
                </div>
              )}
              <span className="pb-2 text-sm font-medium text-slate-400">–</span>
              <div className="min-w-[136px] flex-1 sm:max-w-[170px]">
                <label className={labelClass}>{lang === 'vi' ? 'Kết thúc' : 'End'}</label>
                <DatePicker
                  value={form.endDate || form.date}
                  onChange={endDate => setForm(current => ({
                    ...current,
                    endDate: endDate < current.date ? current.date : endDate,
                  }))}
                  open={openPicker === 'end-date'}
                  onOpenChange={next => setOpenPicker(next ? 'end-date' : null)}
                />
              </div>
              {!form.allDay && (
                <div className="w-[106px]">
                  <label className={labelClass}>{t('calendar.end')}</label>
                  <TimePicker
                    value={form.endTime}
                    onChange={endTime => setForm(current => ({ ...current, endTime }))}
                    open={openPicker === 'end'}
                    onOpenChange={next => setOpenPicker(next ? 'end' : null)}
                  />
                </div>
              )}
            </div>

            <div className="flex flex-wrap items-center gap-3">
              <label className="inline-flex cursor-pointer items-center gap-2 text-[13px] font-medium text-slate-700 dark:text-slate-200">
              <input
                type="checkbox"
                checked={form.allDay}
                onChange={event => {
                  const allDay = event.target.checked;
                  setForm(current => ({ ...current, allDay }));
                  if (allDay) setOpenPicker(null);
                }}
                  className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500"
              />
                <span>{t('calendar.allDay')}</span>
              </label>
              <div className="w-full sm:w-[320px]">
              <Select
                value={selectedRecurrenceOption}
                onChange={handleRecurrenceChange}
                options={recurrenceOptions.map(option => ({ value: option.id, label: option.label }))}
                className="h-9 bg-slate-50 text-[13px] dark:bg-slate-800"
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
                  {lang === 'vi' ? 'Chi tiết sự kiện' : 'Event details'}
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
                      placeholder={lang === 'vi' ? 'Thêm địa điểm' : 'Add location'}
                      onChange={event => setForm(current => ({ ...current, location: event.target.value }))}
                    />
                  </div>
                </div>

                <div className="flex gap-4">
                  <Bell className={rowIconClass} />
                  <div className="min-w-0 flex-1">
                    <div className="mb-2 flex items-center justify-between gap-3">
                      <label className="text-[12px] font-semibold text-slate-500 dark:text-slate-400">
                        {lang === 'vi' ? 'Thông báo' : 'Notifications'}
                      </label>
                      <button
                        type="button"
                        onClick={addReminder}
                        className="inline-flex items-center gap-1 text-[12px] font-semibold text-brand-600 transition hover:text-brand-700 dark:text-brand-400"
                      >
                        <Plus className="h-3.5 w-3.5" />
                        {lang === 'vi' ? 'Thêm thông báo' : 'Add notification'}
                      </button>
                    </div>

                    <div className="space-y-2">
                      {(!form.reminders || form.reminders.length === 0) && (
                        <p className="rounded-lg bg-slate-50 px-3 py-2.5 text-[12.5px] text-slate-400 dark:bg-slate-800/60 dark:text-slate-500">
                          {lang === 'vi' ? 'Không có thông báo' : 'No notifications'}
                        </p>
                      )}
                      {(form.reminders || []).map((reminder, index) => {
                        const showTimeOfDay = reminder.offsetUnit === 'Days' || reminder.offsetUnit === 'Weeks';
                        return (
                          <div key={`${reminder.id ?? 'new'}-${index}`} className="rounded-xl border border-slate-200 bg-slate-50/80 p-2.5 dark:border-slate-700 dark:bg-slate-800/60">
                            <div className="grid items-center gap-2 sm:grid-cols-[minmax(160px,1fr)_82px_minmax(150px,1fr)_36px]">
                            <Select
                              value={reminder.reminderType ?? 'GooglePopup'}
                              onChange={value => updateReminder(index, { reminderType: value as ReminderType })}
                              options={[
                                { value: 'InApp', label: lang === 'vi' ? 'Thông báo trong ứng dụng' : 'In-app notification' },
                                { value: 'GooglePopup', label: lang === 'vi' ? 'Thông báo Google' : 'Google notification' },
                                { value: 'GoogleEmail', label: 'Email' },
                              ]}
                              className="h-10 bg-white text-[12.5px] dark:bg-slate-900"
                            />
                            <input
                              type="number"
                              min="1"
                              max="999"
                              value={reminder.offsetValue}
                              onChange={event => updateReminder(index, { offsetValue: Number.parseInt(event.target.value, 10) || 1 })}
                              aria-label={lang === 'vi' ? 'Thời lượng nhắc trước' : 'Reminder lead time'}
                              className="h-10 w-full rounded-lg border border-slate-200 bg-white px-2 text-center text-[13px] font-medium text-slate-700 outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/15 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200"
                            />
                            <Select
                              value={reminder.offsetUnit}
                              onChange={value => {
                                const offsetUnit = value as EventReminderFormValue['offsetUnit'];
                                updateReminder(index, {
                                  offsetUnit,
                                  timeOfDay: offsetUnit === 'Days' || offsetUnit === 'Weeks' ? (reminder.timeOfDay || '09:00') : undefined,
                                });
                              }}
                              options={[
                                { value: 'Minutes', label: lang === 'vi' ? 'phút trước' : 'minutes before' },
                                { value: 'Hours', label: lang === 'vi' ? 'giờ trước' : 'hours before' },
                                { value: 'Days', label: lang === 'vi' ? 'ngày trước' : 'days before' },
                                { value: 'Weeks', label: lang === 'vi' ? 'tuần trước' : 'weeks before' },
                              ]}
                              className="h-10 bg-white text-[12.5px] dark:bg-slate-900"
                            />
                            <button
                              type="button"
                              onClick={() => removeReminder(index)}
                              aria-label={lang === 'vi' ? 'Xóa thông báo' : 'Remove notification'}
                              className="flex h-9 w-9 items-center justify-center rounded-full text-slate-400 transition hover:bg-rose-50 hover:text-rose-600 dark:hover:bg-rose-500/10 dark:hover:text-rose-400"
                            >
                              <X className="h-4 w-4" />
                            </button>
                            </div>
                            {showTimeOfDay && (
                              <div className="mt-2 flex items-center gap-2 border-t border-slate-200 pt-2 dark:border-slate-700">
                                <span className="text-[12px] text-slate-500 dark:text-slate-400">
                                  {lang === 'vi' ? 'Vào lúc' : 'At'}
                                </span>
                                <input
                                  type="time"
                                  value={reminder.timeOfDay || '09:00'}
                                  onChange={event => updateReminder(index, { timeOfDay: event.target.value })}
                                  className="h-9 rounded-lg border border-slate-200 bg-white px-3 text-[12.5px] font-medium text-slate-700 outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/15 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200"
                                />
                              </div>
                            )}
                          </div>
                        );
                      })}
                    </div>
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
                    />
                    {connections.length === 0 && (
                      <p className="mt-1.5 text-[12px] text-amber-600 dark:text-amber-400">{t('calendar.noConnection')}</p>
                    )}
                  </div>
                </div>

                <div className="flex gap-4">
                  <Paperclip className={rowIconClass} />
                  <div className="min-w-0 flex-1">
                    <button
                      type="button"
                      disabled={!form.connectionId}
                      onClick={() => setDrivePickerOpen(true)}
                      className="inline-flex h-9 items-center gap-2 rounded-lg px-2 text-[13px] font-semibold text-brand-600 transition hover:bg-brand-50 hover:text-brand-700 disabled:text-slate-400 dark:text-brand-400 dark:hover:bg-brand-500/10"
                    >
                      <DriveIcon className="h-5 w-5 shrink-0" />
                      <span>{lang === 'vi' ? 'Thêm tệp từ Google Drive' : 'Add Google Drive attachment'}</span>
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
                  {lang === 'vi' ? 'Khách mời' : 'Guests'}
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
                          setGuestSuggestOpen(true);
                        }}
                        onFocus={() => setGuestSuggestOpen(true)}
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
                            aria-label={lang === 'vi' ? `Xóa ${email}` : `Remove ${email}`}
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
                    {lang === 'vi' ? 'Quyền của khách' : 'Guest permissions'}
                  </h4>
                  <div className="space-y-1">
                    {[
                      { key: 'guestsCanModify', vi: 'Sửa đổi sự kiện', en: 'Modify event' },
                      { key: 'guestsCanInviteOthers', vi: 'Mời người khác', en: 'Invite others' },
                      { key: 'guestsCanSeeOtherGuests', vi: 'Xem danh sách khách', en: 'See guest list' },
                    ].map(permission => (
                      <label
                        key={permission.key}
                        className="flex cursor-pointer items-center gap-3 rounded-lg px-2 py-2 text-[13px] text-slate-700 transition hover:bg-slate-50 dark:text-slate-200 dark:hover:bg-slate-800/70"
                      >
                        <input
                          type="checkbox"
                          checked={form[permission.key as 'guestsCanModify' | 'guestsCanInviteOthers' | 'guestsCanSeeOtherGuests']}
                          onChange={event => setForm(current => ({
                            ...current,
                            [permission.key]: event.target.checked,
                          }))}
                          className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500"
                        />
                        <span>{lang === 'vi' ? permission.vi : permission.en}</span>
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
            aria-label={lang === 'vi' ? 'Lặp lại tùy chỉnh' : 'Custom recurrence'}
            className="w-full max-w-md overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl dark:border-slate-700 dark:bg-slate-900"
            onMouseDown={event => event.stopPropagation()}
          >
            <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4 dark:border-slate-800">
              <h3 className="text-[16px] font-semibold text-slate-900 dark:text-slate-100">
                {lang === 'vi' ? 'Lặp lại tùy chỉnh' : 'Custom recurrence'}
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
                <label className={labelClass}>{lang === 'vi' ? 'Lặp lại mỗi' : 'Repeat every'}</label>
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
                      { value: 'DAILY', label: lang === 'vi' ? 'ngày' : 'day(s)' },
                      { value: 'WEEKLY', label: lang === 'vi' ? 'tuần' : 'week(s)' },
                      { value: 'MONTHLY', label: lang === 'vi' ? 'tháng' : 'month(s)' },
                      { value: 'YEARLY', label: lang === 'vi' ? 'năm' : 'year(s)' },
                    ]}
                    className="h-10 bg-slate-50 text-[13px] dark:bg-slate-800"
                  />
                  </div>
                </div>
              </div>

              {customRecurrence.frequency === 'WEEKLY' && (
                <div>
                  <label className={labelClass}>{lang === 'vi' ? 'Lặp lại vào' : 'Repeat on'}</label>
                  <div className="flex flex-wrap gap-2">
                    {RRULE_WEEK_DAYS.map((day, index) => {
                      const labels = lang === 'vi'
                        ? ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN']
                        : ['M', 'T', 'W', 'T', 'F', 'S', 'S'];
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
                <label className={labelClass}>{lang === 'vi' ? 'Kết thúc' : 'Ends'}</label>
                <div className="space-y-3 text-[13px] text-slate-700 dark:text-slate-200">
                  <label className="flex cursor-pointer items-center gap-3">
                    <input
                      type="radio"
                      name="recurrence-end"
                      checked={customRecurrence.endType === 'never'}
                      onChange={() => setCustomRecurrence(current => ({ ...current, endType: 'never' }))}
                      className="h-4 w-4 text-brand-600 focus:ring-brand-500"
                    />
                    {lang === 'vi' ? 'Không bao giờ' : 'Never'}
                  </label>
                  <label className="flex cursor-pointer flex-wrap items-center gap-3">
                    <input
                      type="radio"
                      name="recurrence-end"
                      checked={customRecurrence.endType === 'until'}
                      onChange={() => setCustomRecurrence(current => ({ ...current, endType: 'until' }))}
                      className="h-4 w-4 text-brand-600 focus:ring-brand-500"
                    />
                    <span className="min-w-16">{lang === 'vi' ? 'Vào ngày' : 'On'}</span>
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
                      className="h-4 w-4 text-brand-600 focus:ring-brand-500"
                    />
                    <span className="min-w-16">{lang === 'vi' ? 'Sau' : 'After'}</span>
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
                    <span>{lang === 'vi' ? 'lần' : 'occurrences'}</span>
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

