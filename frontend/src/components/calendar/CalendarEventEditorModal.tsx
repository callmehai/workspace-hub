import { useEffect, useState } from 'react';
import { CalendarDays, Loader2, X, FileText, CheckSquare } from 'lucide-react';
import { useQuery } from '@tanstack/react-query';
import type { ConnectionDto } from '../../lib/connectionsApi';
import { useI18n } from '../../hooks/useI18n';
import { Select } from '../Select';
import { itemsApi } from '../../lib/itemsApi';
import { GoogleDrivePickerModal } from '../drive/GoogleDrivePickerModal';
import { DriveIcon } from '../../lib/brandIcons';

export interface CalendarEventFormValue {
  connectionId: string;
  title: string;
  date: string;
  allDay: boolean;
  startTime: string;
  endTime: string;
  location: string;
  attendees: string;
  calendarType: 'event' | 'task';
  description: string;
  driveItemIds: string[];
}

interface CalendarEventEditorModalProps {
  open: boolean;
  mode: 'create' | 'edit';
  initialValue: CalendarEventFormValue;
  connections: ConnectionDto[];
  folderName?: string | null;
  saving?: boolean;
  onClose: () => void;
  onSubmit: (value: CalendarEventFormValue) => void;
}

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function CalendarEventEditorModal({
  open,
  mode,
  initialValue,
  connections,
  folderName,
  saving = false,
  onClose,
  onSubmit,
}: CalendarEventEditorModalProps) {
  const { t, lang } = useI18n();
  const [form, setForm] = useState(initialValue);
  const [error, setError] = useState('');
  const [drivePickerOpen, setDrivePickerOpen] = useState(false);

  // Fetch drive files for attachments
  const { data: driveFilesData } = useQuery({
    queryKey: ['items', 'drive-files', form.connectionId],
    queryFn: () => itemsApi.getItems({ types: ['File'], limit: 100 }),
    enabled: open && !!form.connectionId,
  });
  const driveFiles = driveFilesData?.items || [];

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
    
    if (form.calendarType === 'event' && form.attendees) {
      const attendees = form.attendees.split(',').map(x => x.trim()).filter(Boolean);
      const invalid = attendees.filter(email => !EMAIL_RE.test(email));
      if (invalid.length > 0) {
        setError(t('calendar.invalidEmails', { emails: invalid.join(', ') }));
        return;
      }
    }
    
    onSubmit({ ...form, title });
  };

  const baseInputClass = 'w-full rounded-lg border border-slate-200 bg-white px-3 text-[13px] text-slate-900 outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/15 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100';
  const inputClass = `${baseInputClass} h-9`;
  const textareaClass = `${baseInputClass} h-20 py-2 resize-none hide-scrollbar`;
  const labelClass = 'mb-1.5 block text-[12px] font-semibold text-slate-500 dark:text-slate-400';
  const isEvent = form.calendarType === 'event';

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
            <span className={`flex h-9 w-9 items-center justify-center rounded-xl ${isEvent ? 'bg-amber-50 text-amber-600 dark:bg-amber-500/15 dark:text-amber-300' : 'bg-blue-50 text-blue-600 dark:bg-blue-500/15 dark:text-blue-300'}`}>
              {isEvent ? <CalendarDays className="h-5 w-5" /> : <CheckSquare className="h-5 w-5" />}
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
          {mode === 'create' && (
            <div className="flex bg-slate-100 dark:bg-slate-800 p-1 rounded-lg">
              <button 
                type="button" 
                className={`flex flex-1 items-center justify-center gap-2 py-1.5 text-[13px] font-medium rounded-md transition-colors ${form.calendarType === 'event' ? 'bg-white shadow text-slate-900 dark:bg-slate-700 dark:text-slate-100' : 'text-slate-500 hover:text-slate-700 dark:text-slate-400 dark:hover:text-slate-300'}`}
                onClick={() => setForm(f => ({...f, calendarType: 'event'}))}
              >
                <CalendarDays className="w-4 h-4" />
                {t('calendar.typeEvent')}
              </button>
              <button 
                type="button" 
                className={`flex flex-1 items-center justify-center gap-2 py-1.5 text-[13px] font-medium rounded-md transition-colors ${form.calendarType === 'task' ? 'bg-white shadow text-slate-900 dark:bg-slate-700 dark:text-slate-100' : 'text-slate-500 hover:text-slate-700 dark:text-slate-400 dark:hover:text-slate-300'}`}
                onClick={() => setForm(f => ({...f, calendarType: 'task'}))}
              >
                <CheckSquare className="w-4 h-4" />
                {t('calendar.typeTask')}
              </button>
            </div>
          )}

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

          <label className="flex cursor-pointer items-center gap-2.5 rounded-xl border border-slate-200 bg-slate-50 px-3.5 py-3 text-[13px] font-medium text-slate-700 dark:border-slate-700 dark:bg-slate-800/60 dark:text-slate-200">
            <input
              type="checkbox"
              checked={form.allDay}
              onChange={event => setForm(current => ({ ...current, allDay: event.target.checked }))}
              className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500"
            />
            <span>
              {t('calendar.allDay')}
              <span className="ml-1.5 text-[11.5px] font-normal text-slate-400">{t('calendar.allDayHint')}</span>
            </span>
          </label>

          <div className={`grid gap-3 ${form.allDay ? 'grid-cols-1' : 'grid-cols-1 sm:grid-cols-3'}`}>
            <div>
              <label className={labelClass}>{isEvent ? t('calendar.date') : t('calendar.taskDueDate')}</label>
              <input type="date" className={inputClass} value={form.date} onChange={event => setForm(current => ({ ...current, date: event.target.value }))} />
            </div>
            {!form.allDay && (
              <>
                <div>
                  <label className={labelClass}>{t('calendar.start')}</label>
                  <input type="time" step={1800} className={inputClass} value={form.startTime} onChange={event => setForm(current => ({ ...current, startTime: event.target.value }))} />
                </div>
                {isEvent && (
                  <div>
                    <label className={labelClass}>{t('calendar.end')}</label>
                    <input type="time" step={1800} className={inputClass} value={form.endTime} onChange={event => setForm(current => ({ ...current, endTime: event.target.value }))} />
                  </div>
                )}
              </>
            )}
          </div>

          <div>
            <label className={labelClass}>{isEvent ? t('calendar.description') : t('calendar.taskNotes')}</label>
            <textarea 
              className={textareaClass} 
              value={form.description} 
              placeholder={isEvent ? t('calendar.descriptionPlaceholder') : t('calendar.taskNotesPlaceholder')} 
              onChange={event => setForm(current => ({ ...current, description: event.target.value }))} 
            />
          </div>

          {isEvent && (
            <>
              <div>
                <label className={labelClass}>{t('calendar.location')}</label>
                <input className={inputClass} value={form.location} placeholder={t('calendar.optional')} onChange={event => setForm(current => ({ ...current, location: event.target.value }))} />
              </div>

              <div>
                <label className={labelClass}>{t('calendar.attendees')}</label>
                <input className={inputClass} value={form.attendees} placeholder="a@gmail.com, b@gmail.com" onChange={event => setForm(current => ({ ...current, attendees: event.target.value }))} />
              </div>
            </>
          )}
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

            {form.driveItemIds.length > 0 && (
              <div className="flex flex-wrap gap-2 mt-1.5">
                {form.driveItemIds.map(id => {
                  const file = driveFiles.find(f => f.id === id);
                  if (!file) return null;
                  return (
                    <div 
                      key={id}
                      className="inline-flex items-center gap-1.5 pl-2.5 pr-1.5 py-1 rounded-full border border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-800/40 text-[12.5px] max-w-[280px] shadow-sm"
                    >
                      <FileText className="w-3.5 h-3.5 text-brand-500 shrink-0" />
                      <span className="truncate flex-1 text-slate-700 dark:text-slate-200" title={file.title}>
                        {file.title}
                      </span>
                      <button
                        type="button"
                        onClick={() => setForm(curr => ({ ...curr, driveItemIds: curr.driveItemIds.filter(x => x !== id) }))}
                        className="p-0.5 rounded-full text-slate-400 hover:text-rose-500 hover:bg-slate-200 dark:hover:bg-slate-700 transition-colors"
                      >
                        <X className="w-3.5 h-3.5" />
                      </button>
                    </div>
                  );
                })}
              </div>
            )}
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

        <div className="flex justify-end gap-2 border-t border-slate-100 bg-slate-50 px-5 py-3.5 dark:border-slate-800 dark:bg-slate-800/50">
          <button type="button" onClick={onClose} disabled={saving} className="h-9 rounded-lg px-4 text-[13px] font-semibold text-slate-600 hover:bg-slate-200/70 dark:text-slate-300 dark:hover:bg-slate-700">
            {t('common.cancel')}
          </button>
          <button type="button" onClick={submit} disabled={saving || connections.length === 0} className="inline-flex h-9 items-center gap-1.5 rounded-lg bg-brand-600 px-4 text-[13px] font-semibold text-white shadow-sm hover:bg-brand-700 disabled:opacity-50">
            {saving && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            {mode === 'create' ? (isEvent ? t('calendar.createEvent2') : t('calendar.createTask')) : t('common.save')}
          </button>
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

