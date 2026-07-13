import { useEffect, useMemo, useState } from 'react';
import {
  Bell,
  BriefcaseBusiness,
  CalendarDays,
  Check,
  ChevronDown,
  Clock3,
  ExternalLink,
  FileText,
  Loader2,
  LockKeyhole,
  MapPin,
  Paperclip,
  Palette,
  Plus,
  Trash2,
  Users,
  Video,
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
import { EmailChipsInput } from '../EmailChipsInput';
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

const GOOGLE_EVENT_COLORS = [
  { id: '1', color: '#7986cb', label: 'Lavender' },
  { id: '2', color: '#33b679', label: 'Sage' },
  { id: '3', color: '#8e24aa', label: 'Grape' },
  { id: '4', color: '#e67c73', label: 'Flamingo' },
  { id: '5', color: '#f6bf26', label: 'Banana' },
  { id: '6', color: '#f4511e', label: 'Tangerine' },
  { id: '7', color: '#039be5', label: 'Peacock' },
  { id: '8', color: '#616161', label: 'Graphite' },
  { id: '9', color: '#3f51b5', label: 'Blueberry' },
  { id: '10', color: '#0b8043', label: 'Basil' },
  { id: '11', color: '#d50000', label: 'Tomato' },
] as const;

interface CustomRecurrenceValue {
  frequency: CustomRecurrenceFrequency;
  interval: number;
  weekDays: string[];
  endType: CustomRecurrenceEnd;
  until: string;
  count: number;
}

const RRULE_WEEK_DAYS = ['MO', 'TU', 'WE', 'TH', 'FR', 'SA', 'SU'] as const;

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
  onClose,
  onSubmit,
}: CalendarEventEditorModalProps) {
  const { t, lang } = useI18n();
  const [form, setForm] = useState(initialValue);
  const [error, setError] = useState('');
  const [drivePickerOpen, setDrivePickerOpen] = useState(false);
  const [openPicker, setOpenPicker] = useState<'date' | 'start' | 'end' | 'end-date' | null>(null);
  const [customRecurrenceOpen, setCustomRecurrenceOpen] = useState(false);
  const [activeTab, setActiveTab] = useState<'details' | 'find-time'>('details');
  const [moreActionsOpen, setMoreActionsOpen] = useState(false);
  const [addGoogleMeet, setAddGoogleMeet] = useState(false);
  const [eventColorId, setEventColorId] = useState('7');
  const [availability, setAvailability] = useState<'busy' | 'free'>('busy');
  const [visibility, setVisibility] = useState<'default' | 'public' | 'private'>('default');
  const [guestPermissions, setGuestPermissions] = useState({
    modifyEvent: false,
    inviteOthers: true,
    seeGuestList: true,
  });
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
      setActiveTab('details');
      setMoreActionsOpen(false);
      setAddGoogleMeet(false);
      setEventColorId('7');
      setAvailability('busy');
      setVisibility('default');
      setGuestPermissions({ modifyEvent: false, inviteOthers: true, seeGuestList: true });
    }
  }

  const suggestConnectionId = useMemo(
    () => (allConnections ? resolveGmailSuggestConnection(allConnections, form.connectionId) : undefined),
    [allConnections, form.connectionId],
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

  useEffect(() => {
    if (!moreActionsOpen) return;
    const closeMenu = (event: MouseEvent) => {
      const target = event.target as HTMLElement;
      if (!target.closest('[data-calendar-actions]')) setMoreActionsOpen(false);
    };
    window.addEventListener('mousedown', closeMenu);
    return () => window.removeEventListener('mousedown', closeMenu);
  }, [moreActionsOpen]);

  if (!open) return null;

  const submit = () => {
    const title = form.title.trim();
    const endDate = form.endDate || form.date;
    if (!form.connectionId || !title || !form.date || !endDate) {
      setError(t('calendar.requiredFields'));
      return;
    }
    if (endDate < form.date || (!form.allDay && (
      !form.startTime
      || !form.endTime
      || (endDate === form.date && form.endTime <= form.startTime)
    ))) {
      setError(t('calendar.timeOrder'));
      return;
    }

    setError('');
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
  const previewHours = Array.from({ length: 12 }, (_, index) => index + 8);
  const startParts = form.startTime.split(':').map(Number);
  const endParts = form.endTime.split(':').map(Number);
  const previewStartMinutes = Math.max(0, ((startParts[0] || 8) - 8) * 60 + (startParts[1] || 0));
  const previewEndMinutes = Math.max(previewStartMinutes + 30, ((endParts[0] || 9) - 8) * 60 + (endParts[1] || 0));
  const previewEventTop = Math.min(690, previewStartMinutes);
  const previewEventHeight = Math.max(30, Math.min(720 - previewEventTop, previewEndMinutes - previewStartMinutes));

  return (
    <div
      className="fixed inset-0 z-[9000] flex items-center justify-center overflow-y-auto bg-slate-950/50 p-3 backdrop-blur-[2px] sm:p-5"
      onMouseDown={() => { if (!saving) onClose(); }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label={mode === 'create' ? t('calendar.createTitle') : t('calendar.editTitle')}
        className="flex max-h-[94vh] w-full max-w-[1120px] flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl dark:border-slate-700 dark:bg-slate-900"
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
              <button
                type="button"
                onClick={submit}
                disabled={saving || connections.length === 0}
                className="inline-flex h-10 items-center gap-1.5 rounded-full bg-brand-600 px-4 text-[13px] font-semibold text-white shadow-sm transition hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50 sm:px-5"
              >
                {saving && <Loader2 className="h-4 w-4 animate-spin" />}
                {t('common.save')}
              </button>

              <div className="relative" data-calendar-actions>
                <button
                  type="button"
                  onClick={() => setMoreActionsOpen(current => !current)}
                  disabled={saving}
                  aria-expanded={moreActionsOpen}
                  aria-label={lang === 'vi' ? 'Thao tác khác' : 'More actions'}
                  className="inline-flex h-10 items-center gap-1.5 rounded-lg px-2.5 text-[12.5px] font-semibold text-slate-600 transition hover:bg-slate-100 disabled:opacity-50 dark:text-slate-300 dark:hover:bg-slate-800 sm:px-3"
                >
                  <span className="hidden sm:inline">{lang === 'vi' ? 'Thao tác khác' : 'More actions'}</span>
                  <ChevronDown className={`h-4 w-4 transition-transform ${moreActionsOpen ? 'rotate-180' : ''}`} />
                </button>

                {moreActionsOpen && (
                  <div className="absolute right-0 top-11 z-50 w-56 overflow-hidden rounded-xl border border-slate-200 bg-white py-1.5 shadow-xl dark:border-slate-700 dark:bg-slate-850">
                    {mode === 'edit' && htmlLink && (
                      <a
                        href={htmlLink}
                        target="_blank"
                        rel="noreferrer"
                        onClick={() => setMoreActionsOpen(false)}
                        className="flex w-full items-center gap-3 px-3.5 py-2.5 text-[13px] text-slate-700 hover:bg-slate-50 dark:text-slate-200 dark:hover:bg-slate-800"
                      >
                        <ExternalLink className="h-4 w-4 text-slate-400" />
                        {lang === 'vi' ? 'Mở trong Google Calendar' : 'Open in Google Calendar'}
                      </a>
                    )}
                    <button
                      type="button"
                      onClick={() => {
                        setForm(current => ({ ...current, title: `${current.title} (${lang === 'vi' ? 'bản sao' : 'copy'})` }));
                        setMoreActionsOpen(false);
                      }}
                      className="flex w-full items-center gap-3 px-3.5 py-2.5 text-left text-[13px] text-slate-700 hover:bg-slate-50 dark:text-slate-200 dark:hover:bg-slate-800"
                    >
                      <CalendarDays className="h-4 w-4 text-slate-400" />
                      {lang === 'vi' ? 'Nhân bản sự kiện' : 'Duplicate event'}
                    </button>
                    {mode === 'edit' && onDelete && (
                      <button
                        type="button"
                        onClick={() => {
                          setMoreActionsOpen(false);
                          onDelete();
                        }}
                        className="flex w-full items-center gap-3 border-t border-slate-100 px-3.5 py-2.5 text-left text-[13px] text-rose-600 hover:bg-rose-50 dark:border-slate-800 dark:text-rose-400 dark:hover:bg-rose-500/10"
                      >
                        <Trash2 className="h-4 w-4" />
                        {t('common.delete')}
                      </button>
                    )}
                  </div>
                )}
              </div>
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
              <select
                value={selectedRecurrenceOption}
                onChange={e => handleRecurrenceChange(e.target.value)}
                aria-label={lang === 'vi' ? 'Lặp lại' : 'Repeat'}
                className="h-9 max-w-full rounded-lg border border-slate-200 bg-slate-50 px-3 text-[13px] font-medium text-slate-700 outline-none transition focus:border-brand-500 focus:bg-white focus:ring-2 focus:ring-brand-500/15 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-200"
              >
                {recurrenceOptions.map(opt => (
                  <option key={opt.id} value={opt.id}>{opt.label}</option>
                ))}
              </select>
            </div>
          </div>
        </header>

        <div className="min-h-0 flex-1 overflow-y-auto bg-slate-50/70 p-4 dark:bg-slate-950/40 sm:p-6">
          <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_320px]">
            <section className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm dark:border-slate-800 dark:bg-slate-900">
              <div className="border-b border-slate-200 px-5 pt-4 dark:border-slate-800 sm:px-6">
                <div className="flex items-center gap-6">
                  <button
                    type="button"
                    onClick={() => setActiveTab('details')}
                    className={`border-b-2 pb-3 text-[13px] font-semibold transition ${activeTab === 'details'
                      ? 'border-brand-600 text-brand-700 dark:border-brand-400 dark:text-brand-300'
                      : 'border-transparent text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200'}`}
                  >
                    {lang === 'vi' ? 'Chi tiết sự kiện' : 'Event details'}
                  </button>
                  <button
                    type="button"
                    onClick={() => setActiveTab('find-time')}
                    className={`border-b-2 pb-3 text-[13px] font-semibold transition ${activeTab === 'find-time'
                      ? 'border-brand-600 text-brand-700 dark:border-brand-400 dark:text-brand-300'
                      : 'border-transparent text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200'}`}
                  >
                    <span className="inline-flex items-center gap-1.5">
                      <Clock3 className="h-3.5 w-3.5" />
                      {lang === 'vi' ? 'Tìm thời gian' : 'Find a time'}
                    </span>
                  </button>
                </div>
              </div>

              {activeTab === 'details' ? (
              <div className="space-y-5 p-5 sm:p-6">
                <div className="flex gap-4">
                  <Video className={`${rowIconClass} ${addGoogleMeet ? 'text-emerald-500 dark:text-emerald-400' : ''}`} />
                  <div className="min-w-0 flex-1 pt-0.5">
                    <button
                      type="button"
                      onClick={() => setAddGoogleMeet(current => !current)}
                      className={`inline-flex h-10 w-full items-center justify-between rounded-lg border px-3 text-left text-[13px] font-semibold transition ${addGoogleMeet
                        ? 'border-emerald-200 bg-emerald-50 text-emerald-700 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-300'
                        : 'border-transparent text-brand-600 hover:bg-brand-50 dark:text-brand-400 dark:hover:bg-brand-500/10'}`}
                    >
                      <span>{addGoogleMeet
                        ? (lang === 'vi' ? 'Đã thêm hội nghị Google Meet' : 'Google Meet conferencing added')
                        : (lang === 'vi' ? 'Thêm hội nghị truyền hình Google Meet' : 'Add Google Meet video conferencing')}</span>
                      {addGoogleMeet && <Check className="h-4 w-4" />}
                    </button>
                    {addGoogleMeet && (
                      <p className="mt-1.5 px-3 text-[11.5px] text-slate-500 dark:text-slate-400">
                        {lang === 'vi' ? 'Đường dẫn Meet sẽ được tạo sau khi kết nối backend.' : 'The Meet link will be generated after backend integration.'}
                      </p>
                    )}
                  </div>
                </div>

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
                          <div key={`${reminder.id ?? 'new'}-${index}`} className="flex flex-wrap items-center gap-2 rounded-lg bg-slate-50 p-2 dark:bg-slate-800/60">
                            <select
                              value={reminder.reminderType ?? 'GooglePopup'}
                              onChange={event => updateReminder(index, { reminderType: event.target.value as ReminderType })}
                              className="h-8 rounded-md border border-slate-200 bg-white px-2 text-[12px] font-medium text-slate-700 outline-none focus:border-brand-500 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200"
                            >
                              <option value="InApp">{lang === 'vi' ? 'Trong ứng dụng' : 'In app'}</option>
                              <option value="GooglePopup">Google popup</option>
                              <option value="GoogleEmail">Google email</option>
                            </select>
                            <input
                              type="number"
                              min="1"
                              max="999"
                              value={reminder.offsetValue}
                              onChange={event => updateReminder(index, { offsetValue: Number.parseInt(event.target.value, 10) || 1 })}
                              className="h-8 w-16 rounded-md border border-slate-200 bg-white px-2 text-center text-[12px] font-medium text-slate-700 outline-none focus:border-brand-500 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200"
                            />
                            <select
                              value={reminder.offsetUnit}
                              onChange={event => {
                                const offsetUnit = event.target.value as EventReminderFormValue['offsetUnit'];
                                updateReminder(index, {
                                  offsetUnit,
                                  timeOfDay: offsetUnit === 'Days' || offsetUnit === 'Weeks' ? (reminder.timeOfDay || '09:00') : undefined,
                                });
                              }}
                              className="h-8 rounded-md border border-slate-200 bg-white px-2 text-[12px] font-medium text-slate-700 outline-none focus:border-brand-500 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200"
                            >
                              <option value="Minutes">{lang === 'vi' ? 'phút trước' : 'minutes before'}</option>
                              <option value="Hours">{lang === 'vi' ? 'giờ trước' : 'hours before'}</option>
                              <option value="Days">{lang === 'vi' ? 'ngày trước' : 'days before'}</option>
                              <option value="Weeks">{lang === 'vi' ? 'tuần trước' : 'weeks before'}</option>
                            </select>
                            {showTimeOfDay && (
                              <input
                                type="time"
                                value={reminder.timeOfDay || '09:00'}
                                onChange={event => updateReminder(index, { timeOfDay: event.target.value })}
                                className="h-8 rounded-md border border-slate-200 bg-white px-2 text-[12px] font-medium text-slate-700 outline-none focus:border-brand-500 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200"
                              />
                            )}
                            <button
                              type="button"
                              onClick={() => removeReminder(index)}
                              aria-label={lang === 'vi' ? 'Xóa thông báo' : 'Remove notification'}
                              className="ml-auto flex h-8 w-8 items-center justify-center rounded-full text-slate-400 transition hover:bg-rose-50 hover:text-rose-600 dark:hover:bg-rose-500/10 dark:hover:text-rose-400"
                            >
                              <X className="h-4 w-4" />
                            </button>
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
                    <div className="flex items-start gap-2">
                      <Select
                        value={form.connectionId}
                        onChange={connectionId => setForm(current => ({ ...current, connectionId }))}
                        options={connections.map(connection => ({
                          value: connection.id,
                          label: `${connection.providerAccountId} · Google Calendar`,
                        }))}
                        placeholder={t('calendar.selectAccount')}
                        disabled={mode === 'edit'}
                        className="h-10 min-w-0 flex-1"
                      />
                      <details className="group relative shrink-0">
                        <summary
                          aria-label={lang === 'vi' ? 'Màu sự kiện' : 'Event color'}
                          className="flex h-10 cursor-pointer list-none items-center gap-2 rounded-lg border border-slate-200 bg-slate-50 px-3 text-slate-600 transition hover:bg-white dark:border-slate-700 dark:bg-slate-800 dark:text-slate-300 dark:hover:bg-slate-750"
                        >
                          <span
                            className="h-5 w-5 rounded-full shadow-sm ring-2 ring-white dark:ring-slate-700"
                            style={{ backgroundColor: GOOGLE_EVENT_COLORS.find(color => color.id === eventColorId)?.color }}
                          />
                          <ChevronDown className="h-3.5 w-3.5 transition-transform group-open:rotate-180" />
                        </summary>
                        <div className="absolute right-0 top-11 z-30 w-52 rounded-xl border border-slate-200 bg-white p-3 shadow-xl dark:border-slate-700 dark:bg-slate-850">
                          <div className="mb-2 flex items-center gap-2 text-[11.5px] font-semibold text-slate-500 dark:text-slate-400">
                            <Palette className="h-3.5 w-3.5" />
                            {lang === 'vi' ? 'Màu sự kiện' : 'Event color'}
                          </div>
                          <div className="grid grid-cols-6 gap-2">
                            {GOOGLE_EVENT_COLORS.map(color => (
                              <button
                                type="button"
                                key={color.id}
                                title={color.label}
                                onClick={event => {
                                  setEventColorId(color.id);
                                  (event.currentTarget.closest('details') as HTMLDetailsElement | null)?.removeAttribute('open');
                                }}
                                className="flex h-7 w-7 items-center justify-center rounded-full transition hover:scale-110 focus:outline-none focus:ring-2 focus:ring-brand-500/40"
                                style={{ backgroundColor: color.color }}
                              >
                                {eventColorId === color.id && <Check className="h-4 w-4 text-white" />}
                              </button>
                            ))}
                          </div>
                        </div>
                      </details>
                    </div>
                    {connections.length === 0 && (
                      <p className="mt-1.5 text-[12px] text-amber-600 dark:text-amber-400">{t('calendar.noConnection')}</p>
                    )}
                  </div>
                </div>

                <div className="flex gap-4">
                  <BriefcaseBusiness className={rowIconClass} />
                  <div className="grid min-w-0 flex-1 gap-2 sm:grid-cols-2">
                    <div>
                      <label className={labelClass}>{lang === 'vi' ? 'Hiển thị là' : 'Show me as'}</label>
                      <select
                        value={availability}
                        onChange={event => setAvailability(event.target.value as 'busy' | 'free')}
                        className={inputClass}
                      >
                        <option value="busy">{lang === 'vi' ? 'Bận' : 'Busy'}</option>
                        <option value="free">{lang === 'vi' ? 'Rảnh' : 'Free'}</option>
                      </select>
                    </div>
                    <div>
                      <label className={labelClass}>{lang === 'vi' ? 'Chế độ hiển thị' : 'Visibility'}</label>
                      <div className="relative">
                        <LockKeyhole className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
                        <select
                          value={visibility}
                          onChange={event => setVisibility(event.target.value as 'default' | 'public' | 'private')}
                          className={`${inputClass} pl-9`}
                        >
                          <option value="default">{lang === 'vi' ? 'Chế độ mặc định' : 'Default visibility'}</option>
                          <option value="public">{lang === 'vi' ? 'Công khai' : 'Public'}</option>
                          <option value="private">{lang === 'vi' ? 'Riêng tư' : 'Private'}</option>
                        </select>
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
              ) : (
                <div className="p-5 sm:p-6">
                  <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
                    <div>
                      <h3 className="text-[14px] font-semibold text-slate-900 dark:text-slate-100">
                        {lang === 'vi' ? 'Lịch trống của khách mời' : 'Guest availability'}
                      </h3>
                      <p className="mt-1 text-[12px] text-slate-500 dark:text-slate-400">
                        {form.date} · {form.allDay ? (lang === 'vi' ? 'Cả ngày' : 'All day') : `${form.startTime} – ${form.endTime}`}
                      </p>
                    </div>
                    <span className="rounded-full bg-amber-50 px-3 py-1.5 text-[11.5px] font-medium text-amber-700 dark:bg-amber-500/10 dark:text-amber-300">
                      {lang === 'vi' ? 'Bản xem trước giao diện' : 'UI preview'}
                    </span>
                  </div>

                  <div className="mb-4 rounded-xl border border-amber-200 bg-amber-50/70 px-3.5 py-3 text-[12px] leading-relaxed text-amber-800 dark:border-amber-500/20 dark:bg-amber-500/10 dark:text-amber-300">
                    {lang === 'vi'
                      ? 'Dữ liệu bận/rảnh thực tế sẽ hiển thị tại đây sau khi kết nối Google Calendar API.'
                      : 'Live free/busy data will appear here after Google Calendar API integration.'}
                  </div>

                  <div className="overflow-x-auto rounded-xl border border-slate-200 dark:border-slate-700">
                    <div className="grid min-w-[676px] grid-cols-[116px_minmax(560px,1fr)] border-b border-slate-200 bg-slate-50 dark:border-slate-700 dark:bg-slate-800/70">
                      <div className="border-r border-slate-200 px-3 py-2.5 text-[11px] font-semibold uppercase tracking-wide text-slate-500 dark:border-slate-700 dark:text-slate-400">
                        {lang === 'vi' ? 'Người tham dự' : 'Attendee'}
                      </div>
                      <div className="grid grid-cols-12">
                        {previewHours.map(hour => (
                          <div key={hour} className="border-r border-slate-200 px-1 py-2.5 text-center text-[10.5px] text-slate-500 last:border-r-0 dark:border-slate-700 dark:text-slate-400">
                            {String(hour).padStart(2, '0')}:00
                          </div>
                        ))}
                      </div>
                    </div>
                    <div>
                      {[
                        connections.find(connection => connection.id === form.connectionId)?.providerAccountId || (lang === 'vi' ? 'Lịch của tôi' : 'My calendar'),
                        ...form.attendees,
                      ].map((attendee, index) => (
                        <div key={`${attendee}-${index}`} className="grid min-w-[676px] grid-cols-[116px_minmax(560px,1fr)] border-b border-slate-100 last:border-b-0 dark:border-slate-800">
                          <div className="flex min-h-14 items-center gap-2 border-r border-slate-200 px-3 dark:border-slate-700">
                            <span className={`h-7 w-7 shrink-0 rounded-full text-center text-[11px] font-bold leading-7 text-white ${index === 0 ? 'bg-brand-500' : 'bg-slate-400'}`}>
                              {attendee.charAt(0).toUpperCase()}
                            </span>
                            <span className="truncate text-[11.5px] font-medium text-slate-700 dark:text-slate-200" title={attendee}>{attendee}</span>
                          </div>
                          <div className="relative grid min-h-14 grid-cols-12 bg-white dark:bg-slate-900">
                            {previewHours.map(hour => <div key={hour} className="border-r border-slate-100 last:border-r-0 dark:border-slate-800" />)}
                            {!form.allDay && index === 0 && (
                              <div
                                className="absolute bottom-1.5 top-1.5 rounded-md border border-brand-500 bg-brand-100/90 px-2 py-1 text-[10.5px] font-semibold text-brand-700 dark:bg-brand-500/20 dark:text-brand-300"
                                style={{
                                  left: `${(previewEventTop / 720) * 100}%`,
                                  width: `${Math.max(4.2, (previewEventHeight / 720) * 100)}%`,
                                }}
                              >
                                <span className="block truncate">{form.title || (lang === 'vi' ? 'Sự kiện mới' : 'New event')}</span>
                              </div>
                            )}
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>

                  {form.attendees.length === 0 && (
                    <div className="mt-4 flex items-center gap-2 rounded-lg bg-slate-50 px-3 py-2.5 text-[12px] text-slate-500 dark:bg-slate-800/60 dark:text-slate-400">
                      <Users className="h-4 w-4" />
                      {lang === 'vi' ? 'Thêm khách mời ở cột bên phải để xem họ trong lịch.' : 'Add guests on the right to include them in the schedule.'}
                    </div>
                  )}
                </div>
              )}
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
                  <label className={labelClass}>{lang === 'vi' ? 'Thêm khách mời' : 'Add guests'}</label>
                  <EmailChipsInput
                    value={form.attendees}
                    onChange={attendees => setForm(current => ({ ...current, attendees }))}
                    connectionId={suggestConnectionId}
                    placeholder={lang === 'vi' ? 'Nhập email khách mời' : 'Enter guest email'}
                    className="mb-0"
                  />
                </div>

                <div className="border-t border-slate-100 pt-4 dark:border-slate-800">
                  <h4 className="mb-2.5 text-[12px] font-semibold text-slate-700 dark:text-slate-200">
                    {lang === 'vi' ? 'Quyền của khách' : 'Guest permissions'}
                  </h4>
                  <div className="space-y-1">
                    {[
                      { key: 'modifyEvent', vi: 'Sửa đổi sự kiện', en: 'Modify event' },
                      { key: 'inviteOthers', vi: 'Mời người khác', en: 'Invite others' },
                      { key: 'seeGuestList', vi: 'Xem danh sách khách', en: 'See guest list' },
                    ].map(permission => (
                      <label
                        key={permission.key}
                        className="flex cursor-pointer items-center gap-3 rounded-lg px-2 py-2 text-[13px] text-slate-700 transition hover:bg-slate-50 dark:text-slate-200 dark:hover:bg-slate-800/70"
                      >
                        <input
                          type="checkbox"
                          checked={guestPermissions[permission.key as keyof typeof guestPermissions]}
                          onChange={event => setGuestPermissions(current => ({
                            ...current,
                            [permission.key]: event.target.checked,
                          }))}
                          className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500"
                        />
                        <span>{lang === 'vi' ? permission.vi : permission.en}</span>
                      </label>
                    ))}
                  </div>
                </div>

                <div className="rounded-xl bg-slate-50 px-3.5 py-3 text-[12px] leading-relaxed text-slate-500 dark:bg-slate-800/60 dark:text-slate-400">
                  {lang === 'vi'
                    ? 'Các quyền mới đang ở chế độ xem trước UI và chưa được lưu xuống Google Calendar.'
                    : 'The new permission controls are UI-only and are not saved to Google Calendar yet.'}
                </div>

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
                  <select
                    value={customRecurrence.frequency}
                    onChange={event => setCustomRecurrence(current => ({
                      ...current,
                      frequency: event.target.value as CustomRecurrenceFrequency,
                    }))}
                    className="h-10 flex-1 rounded-lg border border-slate-200 bg-slate-50 px-3 text-[13px] text-slate-900 outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/15 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100"
                  >
                    <option value="DAILY">{lang === 'vi' ? 'ngày' : 'day(s)'}</option>
                    <option value="WEEKLY">{lang === 'vi' ? 'tuần' : 'week(s)'}</option>
                    <option value="MONTHLY">{lang === 'vi' ? 'tháng' : 'month(s)'}</option>
                    <option value="YEARLY">{lang === 'vi' ? 'năm' : 'year(s)'}</option>
                  </select>
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

