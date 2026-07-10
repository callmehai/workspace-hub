import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { Loader2, FileText, X } from 'lucide-react';
import toast from 'react-hot-toast';
import { itemsApi } from '../../lib/itemsApi';
import { GoogleDrivePickerModal } from '../drive/GoogleDrivePickerModal';
import { DriveIcon } from '../../lib/brandIcons';
import { connectionsApi, type ConnectionDto } from '../../lib/connectionsApi';
import { handleApiError } from '../../lib/errorUtils';
import { markSeen } from '../../lib/seenStore';
import { Select } from '../Select';
import { useI18n } from '../../hooks/useI18n';

interface Props {
  isOpen: boolean;
  onClose: () => void;
}

const EMPTY = { connectionId: '', title: '', start: '', end: '', location: '', attendees: '', driveItemIds: [] as string[] };

/** Modal tạo sự kiện Google Calendar — dùng chung cho cả view Danh sách lẫn Bảng. */
export const CreateEventModal = ({ isOpen, onClose }: Props) => {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { t, lang } = useI18n();
  const [form, setForm] = useState(EMPTY);
  const [drivePickerOpen, setDrivePickerOpen] = useState(false);

  const { data: connections = [] } = useQuery({
    queryKey: ['connections'],
    queryFn: () => connectionsApi.getConnections(),
    enabled: isOpen,
  });
  const gcalConnections = connections.filter(
    (c: ConnectionDto) => c.serviceType.toLowerCase() === 'gcal' && c.status.toLowerCase() === 'active'
  );

  const { data: driveFilesData } = useQuery({
    queryKey: ['items', 'drive-files', form.connectionId],
    queryFn: () => itemsApi.getItems({ types: ['File'], limit: 100 }),
    enabled: isOpen && !!form.connectionId,
  });
  const driveFiles = driveFilesData?.items || [];

  const createEvent = useMutation({
    mutationFn: () => {
      const attendeesArray = form.attendees
        ? form.attendees.split(',').map(e => e.trim()).filter(e => e.length > 0)
        : undefined;
      return itemsApi.createEvent({
        connectionId: form.connectionId,
        title: form.title,
        start: new Date(form.start).toISOString(),
        end: new Date(form.end).toISOString(),
        location: form.location || undefined,
        attendees: attendeesArray,
        driveItemIds: form.driveItemIds.length > 0 ? form.driveItemIds : undefined,
      });
    },
    onSuccess: (created) => {
      markSeen(created.id); // event tự tạo = đã xem
      queryClient.invalidateQueries({ queryKey: ['items'] });
      setForm(EMPTY);
      onClose();
      toast.success(t('createEvent.created'));
    },
    onError: (err) => handleApiError(err, t('createEvent.createFail'), { navigate }),
  });

  const handleCreate = () => {
    if (!form.connectionId || !form.title || !form.start || !form.end) {
      toast.error(t('createEvent.needFields'));
      return;
    }
    if (new Date(form.start) >= new Date(form.end)) {
      toast.error(t('createEvent.timeOrder'));
      return;
    }
    if (form.attendees) {
      const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
      const invalid = form.attendees
        .split(',').map(e => e.trim()).filter(e => e.length > 0)
        .filter(email => !emailRegex.test(email));
      if (invalid.length > 0) {
        toast.error(t('item.invalidEmails', { emails: invalid.join(', ') }));
        return;
      }
    }
    createEvent.mutate();
  };

  if (!isOpen) return null;

  const inputCls = 'w-full bg-white dark:bg-slate-800 border border-slate-300 dark:border-slate-700 rounded-lg py-2 px-3 text-[13px] text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500 transition-shadow';

  return (
    <div className="fixed inset-0 bg-slate-900/40 backdrop-blur-sm flex items-center justify-center p-4 z-50">
      <div className="bg-white dark:bg-slate-900 rounded-xl w-full max-w-lg shadow-xl overflow-hidden flex flex-col">
        <div className="px-5 py-4 border-b border-slate-200 dark:border-slate-800 flex justify-between items-center">
          <h2 className="text-[16px] font-semibold text-slate-900 dark:text-slate-100">{t('createEvent.title')}</h2>
          <button onClick={onClose} className="text-slate-400 dark:text-slate-500 hover:text-slate-600 dark:hover:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 p-1 rounded-lg transition-colors">
            &times;
          </button>
        </div>
        <div className="p-5 space-y-4 max-h-[70vh] overflow-y-auto">
          <div>
            <label className="block text-[13px] font-medium text-slate-700 dark:text-slate-200 mb-1.5">{t('createEvent.account')}</label>
            <Select
              value={form.connectionId}
              onChange={(v) => setForm({ ...form, connectionId: v })}
              options={gcalConnections.map(c => ({ value: c.id, label: `${c.providerAccountId} (${c.provider})` }))}
              placeholder={t('createEvent.selectAccount')}
              className="h-9"
            />
            {gcalConnections.length === 0 && (
              <p className="text-[12px] text-amber-600 dark:text-amber-400 mt-1.5">{t('createEvent.noConn')}</p>
            )}
          </div>
          <div>
            <label className="block text-[13px] font-medium text-slate-700 dark:text-slate-200 mb-1.5">{t('item.eventTitle')}</label>
            <input type="text" value={form.title} onChange={e => setForm({ ...form, title: e.target.value })} className={inputCls} />
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-[13px] font-medium text-slate-700 dark:text-slate-200 mb-1.5">{t('item.start')}</label>
              <input type="datetime-local" value={form.start} onChange={e => setForm({ ...form, start: e.target.value })} className={inputCls} />
            </div>
            <div>
              <label className="block text-[13px] font-medium text-slate-700 dark:text-slate-200 mb-1.5">{t('item.end')}</label>
              <input type="datetime-local" value={form.end} onChange={e => setForm({ ...form, end: e.target.value })} className={inputCls} />
            </div>
          </div>
          <div>
            <label className="block text-[13px] font-medium text-slate-700 dark:text-slate-200 mb-1.5">{t('item.location')}</label>
            <input type="text" value={form.location} onChange={e => setForm({ ...form, location: e.target.value })} className={inputCls} />
          </div>
          <div>
            <label className="block text-[13px] font-medium text-slate-700 dark:text-slate-200 mb-1.5">{t('item.attendeesComma')}</label>
            <input type="text" value={form.attendees} onChange={e => setForm({ ...form, attendees: e.target.value })} className={inputCls} />
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
        </div>
        <div className="px-5 py-4 border-t border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-800 flex justify-end gap-2">
          <button onClick={onClose} className="px-4 py-2 text-[13px] font-medium text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100 hover:bg-slate-200 dark:hover:bg-slate-700 rounded-lg transition-colors">
            {t('common.cancel')}
          </button>
          <button
            onClick={handleCreate}
            disabled={!form.connectionId || !form.title || !form.start || !form.end || createEvent.isPending}
            className="bg-brand-600 hover:bg-brand-700 disabled:bg-brand-300 dark:disabled:bg-brand-800 text-white px-4 py-2 rounded-lg text-[13px] font-medium transition-colors inline-flex items-center gap-2"
          >
            {createEvent.isPending && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
            {createEvent.isPending ? t('createEvent.creating') : t('createEvent.create')}
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
};
