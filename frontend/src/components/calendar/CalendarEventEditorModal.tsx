import { useEffect, useMemo, useState } from 'react';
import { CalendarDays, ExternalLink, Loader2, Trash2, X, FileText, Plus, Bell } from 'lucide-react';
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
    ];
  }, [form.date, lang]);

  const selectedRecurrenceOption = useMemo(() => {
    const currentRrule = form.recurrence?.[0] || '';
    if (!currentRrule) return 'none';
    const matched = recurrenceOptions.find(opt => opt.rrule[0] === currentRrule);
    return matched ? matched.id : 'none';
  }, [form.recurrence, recurrenceOptions]);

  const handleRecurrenceChange = (optionId: string) => {
    const option = recurrenceOptions.find(opt => opt.id === optionId);
    setForm(curr => ({
      ...curr,
      recurrence: option ? option.rrule : []
    }));
  };

  // Reset form khi modal mở lại (parent có thể giữ cùng key, ví dụ ItemDetail).
  const [prevOpen, setPrevOpen] = useState(open);
  if (open !== prevOpen) {
    setPrevOpen(open);
    if (open) {
      setForm(initialValue);
      setOpenPicker(null);
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

  if (!open) return null;

  const submit = () => {
    const title = form.title.trim();
    if (!form.connectionId || !title || !form.date) {
      setError(t('calendar.requiredFields'));
      return;
    }
    if (!form.allDay && (!form.startTime || !form.endTime || form.endTime <= form.startTime)) {
      setError(t('calendar.timeOrder'));
      return;
    }

    onSubmit({ ...form, title });
  };

  const baseInputClass = 'w-full rounded-lg border border-slate-200 bg-white px-3 text-[13px] text-slate-900 outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/15 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100';
  const inputClass = `${baseInputClass} h-9`;
  const textareaClass = `${baseInputClass} h-20 py-2 resize-none hide-scrollbar`;
  const labelClass = 'mb-1.5 block text-[12px] font-semibold text-slate-500 dark:text-slate-400';

  return (
    <div
      className="fixed inset-0 z-[9000] flex items-center justify-center bg-slate-900/45 p-4 backdrop-blur-sm"
      onMouseDown={() => { if (!saving) onClose(); }}
    >
      <div
        role="dialog"
        aria-modal="true"
        className="w-full max-w-lg overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl dark:border-slate-700 dark:bg-slate-900"
        onMouseDown={event => event.stopPropagation()}
      >
        <div className="flex items-center justify-between border-b border-slate-100 px-5 py-4 dark:border-slate-800">
          <div className="flex items-center gap-3">
            <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-amber-50 text-amber-600 dark:bg-amber-500/15 dark:text-amber-300">
              <CalendarDays className="h-5 w-5" />
            </span>
            <div>
              <h2 className="text-[16px] font-semibold text-slate-900 dark:text-slate-100">
                {mode === 'create' ? t('calendar.createTitle') : t('calendar.editTitle')}
              </h2>
              <p className="text-[11.5px] text-slate-400 dark:text-slate-500">{t('calendar.writeBackHint')}</p>
            </div>
          </div>
          <button type="button" onClick={onClose} disabled={saving} className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-slate-800 dark:hover:text-slate-200">
            <X className="h-4.5 w-4.5" />
          </button>
        </div>

        <div className="max-h-[72vh] space-y-4 overflow-y-auto p-5">
          <div>
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
              className="h-9"
            />
            {connections.length === 0 && (
              <p className="mt-1.5 text-[12px] text-amber-600 dark:text-amber-400">{t('calendar.noConnection')}</p>
            )}
          </div>

          <div>
            <label className={labelClass}>{t('calendar.eventTitle')}</label>
            <input className={inputClass} value={form.title} autoFocus onChange={event => setForm(current => ({ ...current, title: event.target.value }))} />
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <label className="flex cursor-pointer items-center gap-2.5 rounded-xl border border-slate-200 bg-slate-50 px-3.5 py-3 text-[13px] font-medium text-slate-700 dark:border-slate-700 dark:bg-slate-800/60 dark:text-slate-200">
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
              <span>
                {t('calendar.allDay')}
                <span className="ml-1.5 text-[11.5px] font-normal text-slate-400">{t('calendar.allDayHint')}</span>
              </span>
            </label>

            <div>
              <select
                value={selectedRecurrenceOption}
                onChange={e => handleRecurrenceChange(e.target.value)}
                className="w-full h-[46px] rounded-xl border border-slate-200 bg-white px-3.5 text-[13px] font-medium text-slate-700 outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/15 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-200"
              >
                {recurrenceOptions.map(opt => (
                  <option key={opt.id} value={opt.id}>{opt.label}</option>
                ))}
              </select>
            </div>
          </div>

          <div className="space-y-3">
            {/* Start Date & Time */}
            <div className="flex flex-wrap items-center gap-3">
              <div className="w-full sm:w-auto min-w-[135px] flex-1 sm:flex-none">
                <label className={labelClass}>{lang === 'vi' ? 'Ngày bắt đầu' : 'Start Date'}</label>
                <DatePicker
                  value={form.date}
                  onChange={date => setForm(current => ({ 
                    ...current, 
                    date,
                    endDate: current.endDate < date ? date : current.endDate
                  }))}
                  open={openPicker === 'date'}
                  onOpenChange={next => setOpenPicker(next ? 'date' : null)}
                />
              </div>

              {!form.allDay && (
                <div className="w-full sm:w-auto min-w-[95px] flex-1 sm:flex-none">
                  <label className={labelClass}>{t('calendar.start')}</label>
                  <TimePicker
                    value={form.startTime}
                    onChange={startTime => setForm(current => ({ ...current, startTime }))}
                    open={openPicker === 'start'}
                    onOpenChange={next => setOpenPicker(next ? 'start' : null)}
                  />
                </div>
              )}

              <div className="pt-5 text-sm font-semibold text-slate-400 dark:text-slate-500">
                {lang === 'vi' ? 'tới' : 'to'}
              </div>
            </div>

            {/* End Date & Time */}
            <div className="flex flex-wrap items-center gap-3">
              <div className="w-full sm:w-auto min-w-[135px] flex-1 sm:flex-none">
                <label className={labelClass}>{lang === 'vi' ? 'Ngày kết thúc' : 'End Date'}</label>
                <DatePicker
                  value={form.endDate || form.date}
                  onChange={endDate => setForm(current => ({ 
                    ...current, 
                    endDate: endDate < current.date ? current.date : endDate
                  }))}
                  open={openPicker === 'end-date'}
                  onOpenChange={next => setOpenPicker(next ? 'end-date' : null)}
                />
              </div>

              {!form.allDay && (
                <div className="w-full sm:w-auto min-w-[95px] flex-1 sm:flex-none">
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
          </div>

          <div>
            <label className={labelClass}>{t('calendar.description')}</label>
            <textarea 
              className={textareaClass} 
              value={form.description} 
              placeholder={t('calendar.descriptionPlaceholder')} 
              onChange={event => setForm(current => ({ ...current, description: event.target.value }))} 
            />
          </div>

          <div>
            <label className={labelClass}>{t('calendar.location')}</label>
            <input className={inputClass} value={form.location} placeholder={t('calendar.optional')} onChange={event => setForm(current => ({ ...current, location: event.target.value }))} />
          </div>

          <div>
            <label className={labelClass}>{t('calendar.attendees')}</label>
            <EmailChipsInput
              value={form.attendees}
              onChange={attendees => setForm(current => ({ ...current, attendees }))}
              connectionId={suggestConnectionId}
              placeholder={t('sendEmail.toPlaceholder')}
              className="mb-0"
            />
          </div>

          <div className="flex flex-col gap-2">
            <button
              type="button"
              disabled={!form.connectionId}
              onClick={() => setDrivePickerOpen(true)}
              className="inline-flex items-center gap-2.5 text-[13px] font-semibold text-brand-600 hover:text-brand-700 disabled:text-slate-400 dark:text-brand-400 dark:hover:text-brand-350 dark:disabled:text-slate-650 self-start transition-colors"
            >
              <DriveIcon className="w-5 h-5 shrink-0" />
              <span>{lang === 'vi' ? 'Thêm tệp đính kèm từ Google Drive' : 'Add a Google Drive attachment'}</span>
            </button>

            {(form.driveItemIds.length > 0 || form.driveAttachments.length > 0) && (
              <div className="flex flex-wrap gap-2 mt-1.5">
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
                      className="inline-flex items-center gap-1.5 pl-2.5 pr-1.5 py-1 rounded-full border border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-800/40 text-[12.5px] max-w-[280px] shadow-sm"
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
                    <div
                      key={a.fileId || a.fileUrl || a.title}
                      className="inline-flex items-center gap-1.5 pl-2.5 pr-1.5 py-1 rounded-full border border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-800/40 text-[12.5px] max-w-[280px] shadow-sm"
                    >
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

          {/* Reminders list */}
          <div className="flex flex-col gap-2.5 pt-2 border-t border-slate-100 dark:border-slate-800">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold text-slate-500 dark:text-slate-400 uppercase tracking-wider flex items-center gap-1.5">
                <Bell className="w-4 h-4 text-slate-400" />
                <span>{lang === 'vi' ? 'Nhắc nhở sự kiện' : 'Event Reminders'}</span>
              </span>
              <button
                type="button"
                onClick={() => {
                  const currentReminders = form.reminders || [];
                  if (currentReminders.length >= 5) {
                    toast.error(lang === 'vi' ? 'Tối đa 5 nhắc nhở' : 'Maximum 5 reminders');
                    return;
                  }
                  setForm(curr => ({
                    ...curr,
                    reminders: [
                      ...currentReminders,
                      { reminderType: 'GooglePopup', offsetValue: 15, offsetUnit: 'Minutes' }
                    ]
                  }));
                }}
                className="inline-flex items-center gap-1 text-xs font-bold text-brand-600 hover:text-brand-700 dark:text-brand-400 dark:hover:text-brand-350 transition-colors"
              >
                <Plus className="w-3.5 h-3.5" />
                <span>{lang === 'vi' ? 'Thêm nhắc nhở' : 'Add Reminder'}</span>
              </button>
            </div>

            <div className="space-y-2">
              {(!form.reminders || form.reminders.length === 0) ? (
                <span className="text-xs text-slate-400 dark:text-slate-500 italic block">
                  {lang === 'vi' ? 'Chưa cấu hình nhắc nhở.' : 'No reminders configured.'}
                </span>
              ) : (
                form.reminders.map((reminder, idx) => {
                  const showTimeOfDay = reminder.offsetUnit === 'Days' || reminder.offsetUnit === 'Weeks';
                  return (
                    <div key={idx} className="flex flex-wrap items-center gap-2 p-2.5 rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-50/50 dark:bg-slate-900/30">
                      {/* Reminder type */}
                      <select
                        value={reminder.reminderType ?? 'GooglePopup'}
                        onChange={e => {
                          const val = e.target.value as ReminderType;
                          setForm(curr => {
                            const updated = [...(curr.reminders || [])];
                            updated[idx] = { ...updated[idx], reminderType: val };
                            return { ...curr, reminders: updated };
                          });
                        }}
                        className="bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-lg px-2.5 py-1 text-xs font-semibold text-slate-700 dark:text-slate-200 focus:outline-none"
                      >
                        <option value="InApp">{lang === 'vi' ? 'Trong ứng dụng' : 'In app'}</option>
                        <option value="GooglePopup">Google popup</option>
                        <option value="GoogleEmail">Google email</option>
                      </select>

                      {/* Offset Value */}
                      <input
                        type="number"
                        min="1"
                        max="999"
                        value={reminder.offsetValue}
                        onChange={e => {
                          const val = parseInt(e.target.value) || 1;
                          setForm(curr => {
                            const updated = [...(curr.reminders || [])];
                            updated[idx] = { ...updated[idx], offsetValue: val };
                            return { ...curr, reminders: updated };
                          });
                        }}
                        className="w-16 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-lg px-2 py-1 text-xs font-semibold text-center text-slate-700 dark:text-slate-200 focus:outline-none"
                      />

                      {/* Offset Unit */}
                      <select
                        value={reminder.offsetUnit}
                        onChange={e => {
                          const val = e.target.value as EventReminderFormValue['offsetUnit'];
                          setForm(curr => {
                            const updated = [...(curr.reminders || [])];
                            updated[idx] = { 
                              ...updated[idx], 
                              offsetUnit: val,
                              timeOfDay: (val === 'Days' || val === 'Weeks') ? '09:00' : undefined
                            };
                            return { ...curr, reminders: updated };
                          });
                        }}
                        className="bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-lg px-2.5 py-1 text-xs font-semibold text-slate-700 dark:text-slate-200 focus:outline-none"
                      >
                        <option value="Minutes">{lang === 'vi' ? 'phút' : 'minutes'}</option>
                        <option value="Hours">{lang === 'vi' ? 'giờ' : 'hours'}</option>
                        <option value="Days">{lang === 'vi' ? 'ngày' : 'days'}</option>
                        <option value="Weeks">{lang === 'vi' ? 'tuần' : 'weeks'}</option>
                      </select>

                      {/* Time of Day */}
                      {showTimeOfDay && (
                        <div className="flex items-center gap-1.5">
                          <span className="text-xs text-slate-400 dark:text-slate-500">{lang === 'vi' ? 'lúc' : 'at'}</span>
                          <input
                            type="time"
                            value={reminder.timeOfDay || '09:00'}
                            onChange={e => {
                              const val = e.target.value;
                              setForm(curr => {
                                const updated = [...(curr.reminders || [])];
                                updated[idx] = { ...updated[idx], timeOfDay: val };
                                return { ...curr, reminders: updated };
                              });
                            }}
                            className="bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-lg px-2 py-0.5 text-xs font-semibold text-slate-700 dark:text-slate-200 focus:outline-none"
                          />
                        </div>
                      )}

                      <span className="text-xs text-slate-400 dark:text-slate-500 ml-auto">{lang === 'vi' ? 'trước' : 'before'}</span>

                      {/* Delete Button */}
                      <button
                        type="button"
                        onClick={() => {
                          setForm(curr => ({
                            ...curr,
                            reminders: (curr.reminders || []).filter((_, i) => i !== idx)
                          }));
                        }}
                        className="p-1 rounded-lg text-slate-400 hover:text-rose-500 hover:bg-slate-100 dark:hover:bg-slate-850 transition-colors"
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    </div>
                  );
                })
              )}
            </div>
          </div>

          {folderName && (
            <div className="rounded-lg bg-brand-50 px-3 py-2 text-[12px] text-brand-700 dark:bg-brand-500/10 dark:text-brand-300">
              {t('calendar.folderAssign', { folder: folderName })}
            </div>
          )}

          {error && (
            <div className="rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-[12.5px] text-rose-700 dark:border-rose-500/20 dark:bg-rose-500/10 dark:text-rose-300">
              {error}
            </div>
          )}
        </div>

        <div className="flex items-center justify-between gap-2 border-t border-slate-100 bg-slate-50 px-5 py-3.5 dark:border-slate-800 dark:bg-slate-800/50">
          <div className="flex gap-2">
            {mode === 'edit' && htmlLink && (
              <a
                href={htmlLink}
                target="_blank"
                rel="noreferrer"
                className="inline-flex h-9 items-center gap-1.5 rounded-lg border border-slate-200 px-3 text-[12.5px] font-semibold text-slate-600 hover:bg-slate-100 dark:border-slate-700 dark:text-slate-300 dark:hover:bg-slate-700"
              >
                <ExternalLink className="h-3.5 w-3.5" />
                GCal
              </a>
            )}
            {mode === 'edit' && onDelete && (
              <button
                type="button"
                onClick={onDelete}
                disabled={saving}
                className="inline-flex h-9 items-center gap-1.5 rounded-lg border border-rose-200 px-3 text-[12.5px] font-semibold text-rose-600 hover:bg-rose-50 disabled:opacity-50 dark:border-rose-500/20 dark:text-rose-400 dark:hover:bg-rose-500/10"
              >
                <Trash2 className="h-3.5 w-3.5" />
                {t('common.delete')}
              </button>
            )}
          </div>
          <div className="flex gap-2">
            <button type="button" onClick={onClose} disabled={saving} className="h-9 rounded-lg px-4 text-[13px] font-semibold text-slate-600 hover:bg-slate-200/70 dark:text-slate-300 dark:hover:bg-slate-700">
              {t('common.cancel')}
            </button>
            <button type="button" onClick={submit} disabled={saving || connections.length === 0} className="inline-flex h-9 items-center gap-1.5 rounded-lg bg-brand-600 px-4 text-[13px] font-semibold text-white shadow-sm hover:bg-brand-700 disabled:opacity-50">
              {saving && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
              {mode === 'create' ? t('calendar.createEvent2') : t('common.save')}
            </button>
          </div>
        </div>
      </div>
      
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

