import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { X, Loader2, Edit3, Trash2, Check } from 'lucide-react';
import { tagsApi } from '../../lib/tagsApi';
import { type TagResponse } from '../../types/items';
import { TAG_COLORS, DEFAULT_TAG_COLOR } from '../../lib/tagColors';
import { handleApiError } from '../../lib/errorUtils';
import { useI18n } from '../../hooks/useI18n';
import { ConfirmDialog } from '../ConfirmDialog';
import toast from 'react-hot-toast';

interface Props {
  isOpen: boolean;
  onClose: () => void;
}

/** Modal quản lý tag: tạo / sửa / xoá tag của user (SCRUM-71). */
export const TagManagerModal: React.FC<Props> = ({ isOpen, onClose }) => {
  const queryClient = useQueryClient();
  const { t } = useI18n();

  const [name, setName] = useState('');
  const [color, setColor] = useState<string>(DEFAULT_TAG_COLOR);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [tagToDelete, setTagToDelete] = useState<TagResponse | null>(null);

  const { data: tags = [], isLoading } = useQuery({ queryKey: ['tags'], queryFn: tagsApi.getTags });

  const resetForm = () => { setName(''); setColor(DEFAULT_TAG_COLOR); setEditingId(null); };

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['tags'] });
    queryClient.invalidateQueries({ queryKey: ['items'] });
  };

  const saveMutation = useMutation({
    mutationFn: () => {
      const payload = { name: name.trim(), color };
      return editingId ? tagsApi.updateTag(editingId, payload) : tagsApi.createTag(payload);
    },
    onSuccess: () => {
      toast.success(editingId ? t('tag.updated') : t('tag.created'));
      invalidate();
      resetForm();
    },
    onError: (err) => handleApiError(err, editingId ? t('tag.updateFail') : t('tag.createFail')),
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => tagsApi.deleteTag(id),
    onSuccess: () => {
      toast.success(t('tag.deleted'));
      invalidate();
      queryClient.invalidateQueries({ queryKey: ['item'] });
      resetForm();
    },
    onError: (err) => handleApiError(err, t('tag.deleteFail')),
  });

  const startEdit = (tag: TagResponse) => { setEditingId(tag.id); setName(tag.name); setColor(tag.color); };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) { toast.error(t('tag.needName')); return; }
    saveMutation.mutate();
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-[70] flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-slate-900/40" onClick={onClose} />

      <div className="relative w-full max-w-[440px] bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-200 dark:border-slate-800 flex flex-col max-h-[85vh]">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100 dark:border-slate-800">
          <h2 className="text-[17px] font-semibold text-slate-900 dark:text-slate-100">{t('tag.manageTitle')}</h2>
          <button
            onClick={onClose}
            className="p-1.5 text-slate-400 dark:text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800 hover:text-slate-900 dark:hover:text-slate-100 rounded-lg transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Create / edit form */}
        <form onSubmit={handleSubmit} className="flex flex-col gap-3 p-5 border-b border-slate-100 dark:border-slate-800">
          <input
            type="text"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={t('tag.namePlaceholder')}
            maxLength={50}
            className="w-full h-[36px] px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-sm text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500 transition-colors"
            disabled={saveMutation.isPending}
            autoFocus
          />
          <div className="flex flex-wrap gap-2">
            {TAG_COLORS.map((c) => (
              <button
                key={c}
                type="button"
                onClick={() => setColor(c)}
                className={`w-6 h-6 rounded-full shrink-0 transition-transform hover:scale-110 ${
                  color === c ? 'ring-2 ring-offset-2 ring-offset-white dark:ring-offset-slate-900 ring-brand-500' : ''
                }`}
                style={{ backgroundColor: c }}
              />
            ))}
          </div>
          <div className="flex justify-end gap-2">
            {editingId && (
              <button
                type="button"
                onClick={resetForm}
                disabled={saveMutation.isPending}
                className="px-3 py-2 text-sm font-medium text-slate-700 dark:text-slate-200 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors"
              >
                {t('common.cancel')}
              </button>
            )}
            <button
              type="submit"
              disabled={saveMutation.isPending || !name.trim()}
              className="px-4 py-2 text-sm font-medium text-white bg-brand-600 rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
            >
              {saveMutation.isPending && <Loader2 className="w-4 h-4 animate-spin" />}
              {editingId ? t('tag.saveChanges') : t('tag.create')}
            </button>
          </div>
        </form>

        {/* Tag list */}
        <div className="flex-1 overflow-y-auto p-3">
          {isLoading ? (
            <div className="flex justify-center py-6 text-slate-400"><Loader2 className="w-5 h-5 animate-spin" /></div>
          ) : tags.length === 0 ? (
            <div className="text-center py-6 text-sm text-slate-400 dark:text-slate-500">{t('tag.empty')}</div>
          ) : (
            <ul className="flex flex-col gap-0.5">
              {tags.map((tag) => (
                <li
                  key={tag.id}
                  className={`flex items-center gap-2.5 px-2.5 py-2 rounded-lg group ${
                    editingId === tag.id ? 'bg-brand-50 dark:bg-brand-500/10' : 'hover:bg-slate-50 dark:hover:bg-slate-800'
                  }`}
                >
                  <span className="w-3 h-3 rounded-full shrink-0" style={{ backgroundColor: tag.color }} />
                  <span className="flex-1 truncate text-sm font-medium text-slate-800 dark:text-slate-100">{tag.name}</span>
                  <span className="text-[11px] text-slate-400 dark:text-slate-500 tabular-nums">{t('tag.itemCount', { n: tag.itemCount })}</span>
                  <button
                    onClick={() => startEdit(tag)}
                    className="p-1.5 rounded-md text-slate-400 hover:text-brand-600 dark:hover:text-brand-400 hover:bg-slate-100 dark:hover:bg-slate-700 opacity-0 group-hover:opacity-100 transition-all"
                    title={t('tag.edit')}
                  >
                    {editingId === tag.id ? <Check className="w-4 h-4 text-brand-600 dark:text-brand-400 opacity-100" /> : <Edit3 className="w-4 h-4" />}
                  </button>
                  <button
                    onClick={() => setTagToDelete(tag)}
                    disabled={deleteMutation.isPending}
                    className="p-1.5 rounded-md text-slate-400 hover:text-rose-500 dark:hover:text-rose-400 hover:bg-slate-100 dark:hover:bg-slate-700 opacity-0 group-hover:opacity-100 transition-all"
                    title={t('tag.delete')}
                  >
                    <Trash2 className="w-4 h-4" />
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>

      <ConfirmDialog
        open={tagToDelete !== null}
        tone="danger"
        title={tagToDelete?.name}
        message={tagToDelete ? t('tag.confirmDelete', { name: tagToDelete.name }) : ''}
        confirmLabel={t('common.delete')}
        loading={deleteMutation.isPending}
        onConfirm={() => {
          if (tagToDelete) {
            deleteMutation.mutate(tagToDelete.id, { onSettled: () => setTagToDelete(null) });
          }
        }}
        onCancel={() => setTagToDelete(null)}
      />
    </div>
  );
};
