import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { Loader2 } from 'lucide-react';
import toast from 'react-hot-toast';
import { itemsApi } from '../../lib/itemsApi';
import { connectionsApi, type ConnectionDto } from '../../lib/connectionsApi';
import { handleApiError } from '../../lib/errorUtils';

interface Props {
  isOpen: boolean;
  onClose: () => void;
}

const EMPTY = { connectionId: '', title: '', start: '', end: '', location: '', attendees: '' };

/** Modal tạo sự kiện Google Calendar — dùng chung cho cả view Danh sách lẫn Bảng. */
export const CreateEventModal = ({ isOpen, onClose }: Props) => {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [form, setForm] = useState(EMPTY);

  const { data: connections = [] } = useQuery({
    queryKey: ['connections'],
    queryFn: () => connectionsApi.getConnections(),
    enabled: isOpen,
  });
  const gcalConnections = connections.filter(
    (c: ConnectionDto) => c.serviceType.toLowerCase() === 'gcal' && c.status.toLowerCase() === 'active'
  );

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
      });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['items'] });
      setForm(EMPTY);
      onClose();
      toast.success('Đã tạo sự kiện');
    },
    onError: (err) => handleApiError(err, 'Lỗi tạo sự kiện', { navigate }),
  });

  const handleCreate = () => {
    if (!form.connectionId || !form.title || !form.start || !form.end) {
      toast.error('Vui lòng điền đủ thông tin');
      return;
    }
    if (new Date(form.start) >= new Date(form.end)) {
      toast.error('Bắt đầu phải trước kết thúc');
      return;
    }
    if (form.attendees) {
      const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
      const invalid = form.attendees
        .split(',').map(e => e.trim()).filter(e => e.length > 0)
        .filter(email => !emailRegex.test(email));
      if (invalid.length > 0) {
        toast.error(`Email không hợp lệ: ${invalid.join(', ')}`);
        return;
      }
    }
    createEvent.mutate();
  };

  if (!isOpen) return null;

  const inputCls = 'w-full bg-white border border-slate-300 rounded-lg py-2 px-3 text-[13px] text-slate-900 focus:outline-none focus:ring-2 focus:ring-indigo-500/20 focus:border-indigo-500 transition-shadow';

  return (
    <div className="fixed inset-0 bg-slate-900/40 backdrop-blur-sm flex items-center justify-center p-4 z-50">
      <div className="bg-white rounded-xl w-full max-w-lg shadow-xl overflow-hidden flex flex-col">
        <div className="px-5 py-4 border-b border-slate-200 flex justify-between items-center">
          <h2 className="text-[16px] font-semibold text-slate-900">Tạo sự kiện Calendar mới</h2>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600 hover:bg-slate-100 p-1 rounded-lg transition-colors">
            &times;
          </button>
        </div>
        <div className="p-5 space-y-4 max-h-[70vh] overflow-y-auto">
          <div>
            <label className="block text-[13px] font-medium text-slate-700 mb-1.5">Tài khoản Google Calendar</label>
            <select
              value={form.connectionId}
              onChange={e => setForm({ ...form, connectionId: e.target.value })}
              className={inputCls}
            >
              <option value="">-- Chọn tài khoản --</option>
              {gcalConnections.map(c => (
                <option key={c.id} value={c.id}>{c.providerAccountId} ({c.provider})</option>
              ))}
            </select>
            {gcalConnections.length === 0 && (
              <p className="text-[12px] text-amber-600 mt-1.5">Chưa có kết nối Google Calendar hợp lệ.</p>
            )}
          </div>
          <div>
            <label className="block text-[13px] font-medium text-slate-700 mb-1.5">Tiêu đề sự kiện</label>
            <input type="text" value={form.title} onChange={e => setForm({ ...form, title: e.target.value })} className={inputCls} />
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-[13px] font-medium text-slate-700 mb-1.5">Bắt đầu</label>
              <input type="datetime-local" value={form.start} onChange={e => setForm({ ...form, start: e.target.value })} className={inputCls} />
            </div>
            <div>
              <label className="block text-[13px] font-medium text-slate-700 mb-1.5">Kết thúc</label>
              <input type="datetime-local" value={form.end} onChange={e => setForm({ ...form, end: e.target.value })} className={inputCls} />
            </div>
          </div>
          <div>
            <label className="block text-[13px] font-medium text-slate-700 mb-1.5">Địa điểm</label>
            <input type="text" value={form.location} onChange={e => setForm({ ...form, location: e.target.value })} className={inputCls} />
          </div>
          <div>
            <label className="block text-[13px] font-medium text-slate-700 mb-1.5">Người tham gia (Email cách nhau bởi dấu phẩy)</label>
            <input type="text" value={form.attendees} onChange={e => setForm({ ...form, attendees: e.target.value })} className={inputCls} />
          </div>
        </div>
        <div className="px-5 py-4 border-t border-slate-200 bg-slate-50 flex justify-end gap-2">
          <button onClick={onClose} className="px-4 py-2 text-[13px] font-medium text-slate-600 hover:text-slate-900 hover:bg-slate-200 rounded-lg transition-colors">
            Hủy
          </button>
          <button
            onClick={handleCreate}
            disabled={!form.connectionId || !form.title || !form.start || !form.end || createEvent.isPending}
            className="bg-indigo-600 hover:bg-indigo-700 disabled:bg-indigo-300 text-white px-4 py-2 rounded-lg text-[13px] font-medium transition-colors inline-flex items-center gap-2"
          >
            {createEvent.isPending && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
            {createEvent.isPending ? 'Đang tạo...' : 'Tạo sự kiện'}
          </button>
        </div>
      </div>
    </div>
  );
};
