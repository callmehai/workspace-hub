import React, { useState, useEffect } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { X, Loader2 } from 'lucide-react';
import { foldersApi } from '../../lib/itemsApi';
import { type FolderResponse } from '../../types/items';
import { handleApiError } from '../../lib/errorUtils';
import toast from 'react-hot-toast';
import { useI18n } from '../../hooks/useI18n';

interface FolderModalProps {
  isOpen: boolean;
  onClose: () => void;
  folder?: FolderResponse; // If provided, we are editing. Otherwise, creating.
}

const COLORS = [
  '#94a3b8', '#f87171', '#fb923c', '#fbbf24', '#a3e635',
  '#4ade80', '#34d399', '#2dd4bf', '#38bdf8', '#60a5fa',
  '#818cf8', '#a78bfa', '#c084fc', '#e879f9', '#f472b6'
];

export const FolderModal: React.FC<FolderModalProps> = ({ isOpen, onClose, folder }) => {
  const isEditing = !!folder;
  const queryClient = useQueryClient();
  const { t } = useI18n();

  const [name, setName] = useState('');
  const [color, setColor] = useState(COLORS[0]);

  useEffect(() => {
    if (isOpen) {
      if (folder) {
        // eslint-disable-next-line react-hooks/set-state-in-effect
        setName(folder.name);
        setColor(folder.color || COLORS[0]);
      } else {
        setName('');
        setColor(COLORS[0]);
      }
    }
  }, [isOpen, folder]);

  const mutation = useMutation({
    mutationFn: async (payload: { name: string; color: string; icon: string; sortOrder?: number; isArchived?: boolean }) => {
      if (isEditing && folder) {
        return foldersApi.updateFolder(folder.id, {
          ...payload,
          sortOrder: folder.sortOrder,
          isArchived: folder.isArchived,
        });
      } else {
        return foldersApi.createFolder(payload);
      }
    },
    onSuccess: () => {
      toast.success(isEditing ? t('folder.updated') : t('folder.created'));
      queryClient.invalidateQueries({ queryKey: ['folders'] });
      onClose();
    },
    onError: (err) => {
      handleApiError(err, isEditing ? t('folder.updateFail') : t('folder.createFail'));
    }
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) {
      toast.error(t('folder.needName'));
      return;
    }
    mutation.mutate({ name: name.trim(), color, icon: folder?.icon || 'folder' });
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-slate-900/40" onClick={onClose} />
      
      <div className="relative w-full max-w-[400px] bg-white dark:bg-slate-900 rounded-xl shadow-xl border border-slate-200 dark:border-slate-800 flex flex-col">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100 dark:border-slate-800">
          <h2 className="text-[17px] font-semibold text-slate-900 dark:text-slate-100">
            {isEditing ? t('folder.editTitle') : t('nav.newFolder')}
          </h2>
          <button
            onClick={onClose}
            className="p-1.5 text-slate-400 dark:text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800 hover:text-slate-900 dark:hover:text-slate-100 rounded-lg transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="flex flex-col p-5 gap-5">
          <div className="flex flex-col gap-1.5">
            <label htmlFor="folderName" className="text-sm font-medium text-slate-700 dark:text-slate-200">
              {t('folder.nameLabel')} <span className="text-rose-500">*</span>
            </label>
            <input
              id="folderName"
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder={t('folder.namePlaceholder')}
              className="w-full h-[36px] px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-sm text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500 transition-colors"
              disabled={mutation.isPending}
              autoFocus
            />
          </div>

          <div className="flex flex-col gap-2">
            <label className="text-sm font-medium text-slate-700 dark:text-slate-200">{t('folder.colorLabel')}</label>
            <div className="flex flex-wrap gap-2">
              {COLORS.map((c) => (
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
          </div>

          <div className="flex justify-end gap-2 pt-2">
            <button
              type="button"
              onClick={onClose}
              disabled={mutation.isPending}
              className="px-4 py-2 text-sm font-medium text-slate-700 dark:text-slate-200 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors"
            >
              {t('common.cancel')}
            </button>
            <button
              type="submit"
              disabled={mutation.isPending || !name.trim()}
              className="px-4 py-2 text-sm font-medium text-white bg-brand-600 rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
            >
              {mutation.isPending && <Loader2 className="w-4 h-4 animate-spin" />}
              {isEditing ? t('folder.saveChanges') : t('folder.create')}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
