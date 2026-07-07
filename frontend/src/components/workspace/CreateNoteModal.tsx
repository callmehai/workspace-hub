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
      <div className="bg-white dark:bg-slate-900 rounded-xl w-full max-w-lg shadow-xl overflow-hidden flex flex-col">
        <div className="px-5 py-4 border-b border-slate-200 dark:border-slate-800 flex justify-between items-center">
          <h2 className="text-[16px] font-semibold text-slate-900 dark:text-slate-100">
            Tạo ghi chú mới
            {folder && (
              <span className="ml-2 text-[12px] font-medium text-slate-500 dark:text-slate-400">
                → thư mục "{folder.name}"
              </span>
            )}
          </h2>
          <button onClick={onClose} className="text-slate-400 dark:text-slate-500 hover:text-slate-600 dark:hover:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 p-1 rounded-lg transition-colors">
            &times;
          </button>
        </div>
        <div className="p-5 space-y-4">
          <div>
            <label className="block text-[13px] font-medium text-slate-700 dark:text-slate-200 mb-1.5">Tiêu đề</label>
            <input
              type="text"
              value={form.title}
              onChange={e => setForm({ ...form, title: e.target.value })}
              className="w-full bg-white dark:bg-slate-800 border border-slate-300 dark:border-slate-700 rounded-lg py-2 px-3 text-[13px] text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500 transition-shadow"
              placeholder="Tiêu đề ghi chú..."
            />
          </div>
          <div>
            <label className="block text-[13px] font-medium text-slate-700 dark:text-slate-200 mb-1.5">Nội dung (Markdown)</label>
            <textarea
              value={form.contentMarkdown}
              onChange={e => setForm({ ...form, contentMarkdown: e.target.value })}
              className="w-full h-32 bg-white dark:bg-slate-800 border border-slate-300 dark:border-slate-700 rounded-lg py-2 px-3 text-[13px] text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500 transition-shadow resize-none"
              placeholder="Nội dung ghi chú..."
            ></textarea>
          </div>
        </div>
        <div className="px-5 py-4 border-t border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-800 flex justify-end gap-2">
          <button onClick={onClose} className="px-4 py-2 text-[13px] font-medium text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100 hover:bg-slate-200 dark:hover:bg-slate-700 rounded-lg transition-colors">
            Hủy
          </button>
          <button
            onClick={() => createNote.mutate()}
            disabled={!form.title || !form.contentMarkdown || createNote.isPending}
            className="bg-brand-600 hover:bg-brand-700 disabled:bg-brand-300 dark:disabled:bg-brand-800 text-white px-4 py-2 rounded-lg text-[13px] font-medium transition-colors inline-flex items-center gap-2"
          >
            {createNote.isPending && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
            {createNote.isPending ? 'Đang tạo...' : 'Tạo ghi chú'}
          </button>
        </div>
      </div>
    </div>
  );
};
