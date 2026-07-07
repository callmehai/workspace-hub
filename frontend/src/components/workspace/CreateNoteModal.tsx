import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { Loader2 } from 'lucide-react';
import toast from 'react-hot-toast';
import { itemsApi } from '../../lib/itemsApi';
import { handleApiError } from '../../lib/errorUtils';
import type { FolderResponse } from '../../types/items';

interface Props {
  isOpen: boolean;
  onClose: () => void;
  /** Context thư mục hiện tại — note tạo ra sẽ tự gán vào đây */
  folder: FolderResponse | null;
}

/** Modal tạo ghi chú — dùng chung cho cả view Danh sách lẫn Bảng. */
export const CreateNoteModal = ({ isOpen, onClose, folder }: Props) => {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [form, setForm] = useState({ title: '', contentMarkdown: '' });

  const createNote = useMutation({
    mutationFn: () => itemsApi.createNote({ ...form, folderId: folder?.id }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['items'] });
      queryClient.invalidateQueries({ queryKey: ['folders'] });
      setForm({ title: '', contentMarkdown: '' });
      onClose();
      toast.success('Đã tạo ghi chú');
    },
    onError: (err) => handleApiError(err, 'Lỗi tạo ghi chú', { navigate }),
  });

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-slate-900/40 backdrop-blur-sm flex items-center justify-center p-4 z-50">
      <div className="bg-white rounded-xl w-full max-w-lg shadow-xl overflow-hidden flex flex-col">
        <div className="px-5 py-4 border-b border-slate-200 flex justify-between items-center">
          <h2 className="text-[16px] font-semibold text-slate-900">
            Tạo ghi chú mới
            {folder && (
              <span className="ml-2 text-[12px] font-medium text-slate-500">
                → thư mục "{folder.name}"
              </span>
            )}
          </h2>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600 hover:bg-slate-100 p-1 rounded-lg transition-colors">
            &times;
          </button>
        </div>
        <div className="p-5 space-y-4">
          <div>
            <label className="block text-[13px] font-medium text-slate-700 mb-1.5">Tiêu đề</label>
            <input
              type="text"
              value={form.title}
              onChange={e => setForm({ ...form, title: e.target.value })}
              className="w-full bg-white border border-slate-300 rounded-lg py-2 px-3 text-[13px] text-slate-900 focus:outline-none focus:ring-2 focus:ring-indigo-500/20 focus:border-indigo-500 transition-shadow"
              placeholder="Tiêu đề ghi chú..."
            />
          </div>
          <div>
            <label className="block text-[13px] font-medium text-slate-700 mb-1.5">Nội dung (Markdown)</label>
            <textarea
              value={form.contentMarkdown}
              onChange={e => setForm({ ...form, contentMarkdown: e.target.value })}
              className="w-full h-32 bg-white border border-slate-300 rounded-lg py-2 px-3 text-[13px] text-slate-900 focus:outline-none focus:ring-2 focus:ring-indigo-500/20 focus:border-indigo-500 transition-shadow resize-none"
              placeholder="Nội dung ghi chú..."
            ></textarea>
          </div>
        </div>
        <div className="px-5 py-4 border-t border-slate-200 bg-slate-50 flex justify-end gap-2">
          <button onClick={onClose} className="px-4 py-2 text-[13px] font-medium text-slate-600 hover:text-slate-900 hover:bg-slate-200 rounded-lg transition-colors">
            Hủy
          </button>
          <button
            onClick={() => createNote.mutate()}
            disabled={!form.title || !form.contentMarkdown || createNote.isPending}
            className="bg-indigo-600 hover:bg-indigo-700 disabled:bg-indigo-300 text-white px-4 py-2 rounded-lg text-[13px] font-medium transition-colors inline-flex items-center gap-2"
          >
            {createNote.isPending && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
            {createNote.isPending ? 'Đang tạo...' : 'Tạo ghi chú'}
          </button>
        </div>
      </div>
    </div>
  );
};
