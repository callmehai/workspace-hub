import React, { useEffect, useRef, useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { FolderPlus, FolderMinus, Tag, X, Trash2 } from 'lucide-react';
import { foldersApi, itemsApi } from '../lib/itemsApi';
import { tagsApi } from '../lib/tagsApi';
import { handleApiError } from '../lib/errorUtils';
import { type FolderResponse, type TagResponse } from '../types/items';
import { useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { useI18n } from '../hooks/useI18n';

interface BulkActionBarProps {
  selectedItemIds: Set<string>;
  onClearSelection: () => void;
  mailbox?: string;
}

export const BulkActionBar: React.FC<BulkActionBarProps> = ({ selectedItemIds, onClearSelection, mailbox }) => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { t } = useI18n();
  const [isAdding, setIsAdding] = useState(false);
  const [isRemoving, setIsRemoving] = useState(false);
  const [isTagging, setIsTagging] = useState(false);
  const barRef = useRef<HTMLDivElement>(null);

  // Hết selection (sau khi hành động xong / bấm X) → reset dropdown, tránh lần sau
  // thanh hiện lại đã tự bung listbox vì component chỉ return null chứ không unmount.
  // Điều chỉnh state khi prop đổi ngay trong render (guard prevCount) theo React docs
  // — không dùng effect + setState (vi phạm rule react-hooks/set-state-in-effect).
  const [prevCount, setPrevCount] = useState(selectedItemIds.size);
  if (selectedItemIds.size !== prevCount) {
    setPrevCount(selectedItemIds.size);
    if (selectedItemIds.size === 0) {
      setIsAdding(false);
      setIsRemoving(false);
      setIsTagging(false);
    }
  }

  // Đóng mọi dropdown (thêm/gỡ thư mục, gắn tag) khi click ra ngoài thanh / nhấn Esc.
  useEffect(() => {
    if (!isAdding && !isRemoving && !isTagging) return;
    const closeAll = () => { setIsAdding(false); setIsRemoving(false); setIsTagging(false); };
    const onDocClick = (e: MouseEvent) => {
      if (barRef.current && !barRef.current.contains(e.target as Node)) closeAll();
    };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') closeAll(); };
    document.addEventListener('mousedown', onDocClick);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDocClick);
      document.removeEventListener('keydown', onKey);
    };
  }, [isAdding, isRemoving, isTagging]);

  const { data: folders = [] } = useQuery({
    queryKey: ['folders'],
    queryFn: () => foldersApi.getFolders()
  });

  const { data: tags = [] } = useQuery({ queryKey: ['tags'], queryFn: tagsApi.getTags });

  const invalidateSelected = () => {
    queryClient.invalidateQueries({ queryKey: ['items'] });
    selectedItemIds.forEach(id => queryClient.invalidateQueries({ queryKey: ['item', id] }));
    queryClient.invalidateQueries({ queryKey: ['tags'] });
  };

  // Gắn tag hàng loạt — BE chưa có endpoint bulk nên gọi assign từng item;
  // allSettled để item đã gắn sẵn (409) không làm hỏng cả batch.
  const tagBulkMutation = useMutation({
    mutationFn: async (tagId: string) => {
      await Promise.allSettled(Array.from(selectedItemIds).map(id => tagsApi.assignTag(tagId, id)));
    },
    onSuccess: () => {
      toast.success(t('bulk.taggedN', { n: selectedItemIds.size }));
      invalidateSelected();
      onClearSelection();
    },
    onError: (err) => handleApiError(err, t('tag.assignFail'), { navigate })
  });

  const addBulkMutation = useMutation({
    mutationFn: (folderId: string) => foldersApi.addItemsToFolderBulk(folderId, Array.from(selectedItemIds)),
    onSuccess: () => {
      toast.success(t('bulk.addedN', { n: selectedItemIds.size }));
      queryClient.invalidateQueries({ queryKey: ['items'] });
      selectedItemIds.forEach(id => {
        queryClient.invalidateQueries({ queryKey: ['item', id] });
      });
      onClearSelection();
    },
    onError: (err) => handleApiError(err, t('item.addFolderFail'), { navigate })
  });

  const removeBulkMutation = useMutation({
    mutationFn: (folderId: string) => foldersApi.removeItemsFromFolderBulk(folderId, Array.from(selectedItemIds)),
    onSuccess: () => {
      toast.success(t('bulk.removedN', { n: selectedItemIds.size }));
      queryClient.invalidateQueries({ queryKey: ['items'] });
      selectedItemIds.forEach(id => {
        queryClient.invalidateQueries({ queryKey: ['item', id] });
      });
      onClearSelection();
    },
    onError: (err) => handleApiError(err, t('bulk.removeFail'), { navigate })
  });

  const deleteBulkMutation = useMutation({
    mutationFn: async () => {
      const results = await Promise.allSettled(Array.from(selectedItemIds).map(id => itemsApi.deleteItem(id)));
      const failed = results.filter(r => r.status === 'rejected');
      if (failed.length === results.length && results.length > 0) {
        throw new Error('All failed');
      }
      return failed.length; // return failed count
    },
    onSuccess: (failedCount) => {
      if (failedCount > 0) {
        toast.success(t('bulk.partialDelete') || 'Đã xoá một phần, một số mục bị lỗi.');
      } else {
        toast.success(t('bulk.deletedN', { n: selectedItemIds.size }));
      }
      queryClient.invalidateQueries({ queryKey: ['items'] });
      selectedItemIds.forEach(id => {
        queryClient.invalidateQueries({ queryKey: ['item', id] });
      });
      onClearSelection();
    },
    onError: (err) => handleApiError(err, t('bulk.deleteFail'), { navigate })
  });

  const handleDelete = () => {
    const isTrashOrSpam = mailbox === 'TRASH' || mailbox === 'SPAM';
    const confirmMsg = isTrashOrSpam
      ? t('bulk.deletePermanentlyConfirm', { n: selectedItemIds.size })
      : t('bulk.deleteConfirm', { n: selectedItemIds.size });

    if (window.confirm(confirmMsg)) {
      deleteBulkMutation.mutate();
    }
  };

  if (selectedItemIds.size === 0) return null;

  return (
    <div ref={barRef} className="fixed bottom-6 left-1/2 -translate-x-1/2 z-[100] bg-white dark:bg-slate-800 border-2 border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200 px-5 py-3 rounded-2xl shadow-[0_10px_35px_-5px_rgba(0,0,0,0.15)] flex items-center gap-6 animate-in slide-in-from-bottom-10 fade-in duration-300">
      <div className="flex items-center gap-2.5">
        <span className="inline-flex items-center justify-center min-w-[22px] h-[22px] px-1.5 rounded-full bg-brand-500/15 text-brand-700 dark:text-brand-300 font-bold text-[12.5px] tabular-nums">
          {selectedItemIds.size}
        </span>
        <span className="text-[13.5px] font-semibold text-slate-600 dark:text-slate-400">{t('bulk.selected')}</span>
      </div>

      <div className="h-5 w-[1px] bg-slate-200 dark:bg-slate-700"></div>

      <div className="flex items-center gap-2">
        {/* Add to folder */}
        <div className="relative">
          <button 
            onClick={() => { setIsAdding(!isAdding); setIsRemoving(false); }}
            className={`flex items-center gap-2 text-[13px] font-semibold px-3 py-1.5 rounded-lg transition-all ${
              isAdding
                ? 'bg-brand-50 text-brand-600 dark:bg-brand-500/10 dark:text-brand-400'
                : 'text-slate-600 hover:bg-slate-50 hover:text-slate-900 dark:text-slate-400 dark:hover:bg-slate-700 dark:hover:text-slate-100'
            }`}
          >
            <FolderPlus className="w-4 h-4" />
            {t('bulk.addTo')}
          </button>
          
          {isAdding && (
            <div className="absolute bottom-full left-0 mb-2 w-56 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 shadow-xl rounded-xl py-1.5 text-slate-800 dark:text-slate-200">
              <div className="px-3 py-2 text-[11px] font-semibold text-slate-400 dark:text-slate-500 uppercase tracking-wider">{t('item.addToFolder')}</div>
              <div className="max-h-60 overflow-y-auto">
                {folders.length === 0 ? (
                  <div className="px-4 py-2 text-sm text-slate-500 dark:text-slate-400">{t('bulk.noFolders')}</div>
                ) : (
                  folders.map((f: FolderResponse) => (
                    <button
                      key={f.id}
                      disabled={addBulkMutation.isPending}
                      onClick={() => addBulkMutation.mutate(f.id)}
                      className="w-full text-left px-4 py-2 text-[13px] font-medium hover:bg-slate-50 dark:hover:bg-slate-700 flex items-center gap-2.5 transition-colors"
                    >
                      <span className="w-2 h-2 rounded-full shrink-0" style={{ backgroundColor: f.color || '#94a3b8' }}></span>
                      <span className="truncate">{f.name}</span>
                    </button>
                  ))
                )}
              </div>
            </div>
          )}
        </div>

        {/* Remove from folder */}
        <div className="relative">
          <button 
            onClick={() => { setIsRemoving(!isRemoving); setIsAdding(false); }}
            className={`flex items-center gap-2 text-[13px] font-semibold px-3 py-1.5 rounded-lg transition-all ${
              isRemoving
                ? 'bg-rose-50 text-rose-600 dark:bg-rose-500/10 dark:text-rose-400'
                : 'text-slate-600 hover:bg-slate-50 hover:text-slate-900 dark:text-slate-400 dark:hover:bg-slate-700 dark:hover:text-slate-100'
            }`}
          >
            <FolderMinus className="w-4 h-4" />
            {t('bulk.removeFrom')}
          </button>
          
          {isRemoving && (
            <div className="absolute bottom-full left-0 mb-2 w-56 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 shadow-xl rounded-xl py-1.5 text-slate-800 dark:text-slate-200">
              <div className="px-3 py-2 text-[11px] font-semibold text-slate-400 dark:text-slate-500 uppercase tracking-wider">{t('bulk.removeFromFolder')}</div>
              <div className="max-h-60 overflow-y-auto">
                {folders.length === 0 ? (
                  <div className="px-4 py-2 text-sm text-slate-500 dark:text-slate-400">{t('bulk.noFolders')}</div>
                ) : (
                  folders.map((f: FolderResponse) => (
                    <button
                      key={f.id}
                      disabled={removeBulkMutation.isPending}
                      onClick={() => removeBulkMutation.mutate(f.id)}
                      className="w-full text-left px-4 py-2 text-[13px] font-medium hover:bg-rose-50 hover:text-rose-600 dark:hover:bg-rose-500/10 dark:hover:text-rose-400 flex items-center gap-2.5 transition-colors"
                    >
                      <span className="w-2 h-2 rounded-full shrink-0" style={{ backgroundColor: f.color || '#94a3b8' }}></span>
                      <span className="truncate">{f.name}</span>
                    </button>
                  ))
                )}
              </div>
            </div>
          )}
        </div>

        {/* Gắn tag hàng loạt */}
        <div className="relative">
          <button
            onClick={() => { setIsTagging(!isTagging); setIsAdding(false); setIsRemoving(false); }}
            className={`flex items-center gap-2 text-[13px] font-semibold px-3 py-1.5 rounded-lg transition-all ${
              isTagging
                ? 'bg-brand-50 text-brand-600 dark:bg-brand-500/10 dark:text-brand-400'
                : 'text-slate-600 hover:bg-slate-50 hover:text-slate-900 dark:text-slate-400 dark:hover:bg-slate-700 dark:hover:text-slate-100'
            }`}
          >
            <Tag className="w-4 h-4" />
            {t('bulk.tag')}
          </button>

          {isTagging && (
            <div className="absolute bottom-full left-0 mb-2 w-56 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 shadow-xl rounded-xl py-1.5 text-slate-800 dark:text-slate-200">
              <div className="px-3 py-2 text-[11px] font-semibold text-slate-400 dark:text-slate-500 uppercase tracking-wider">{t('bulk.tag')}</div>
              <div className="max-h-60 overflow-y-auto">
                {tags.length === 0 ? (
                  <div className="px-4 py-2 text-sm text-slate-500 dark:text-slate-400">{t('tag.noneAvailable')}</div>
                ) : (
                  tags.map((tg: TagResponse) => (
                    <button
                      key={tg.id}
                      disabled={tagBulkMutation.isPending}
                      onClick={() => tagBulkMutation.mutate(tg.id)}
                      className="w-full text-left px-4 py-2 text-[13px] font-medium hover:bg-slate-50 dark:hover:bg-slate-700 flex items-center gap-2.5 transition-colors"
                    >
                      <span className="w-2 h-2 rounded-full shrink-0" style={{ backgroundColor: tg.color }}></span>
                      <span className="truncate">{tg.name}</span>
                    </button>
                  ))
                )}
              </div>
            </div>
          )}
        </div>

        {/* Xoá / Xoá vĩnh viễn hàng loạt */}
        <button
          onClick={handleDelete}
          disabled={deleteBulkMutation.isPending}
          className="flex items-center gap-2 text-[13px] font-semibold px-3 py-1.5 rounded-lg text-rose-600 hover:bg-rose-50 dark:text-rose-400 dark:hover:bg-rose-500/10 transition-all disabled:opacity-50"
        >
          <Trash2 className="w-4 h-4" />
          {mailbox === 'TRASH' || mailbox === 'SPAM'
            ? t('bulk.deletePermanently')
            : t('bulk.delete')}
        </button>
      </div>

      <div className="h-5 w-[1px] bg-slate-200 dark:bg-slate-700"></div>

      <button
        onClick={onClearSelection}
        className="p-1.5 hover:bg-slate-100 dark:hover:bg-slate-700 rounded-full text-slate-400 hover:text-slate-600 dark:text-slate-500 dark:hover:text-slate-300 transition-colors"
        title={t('bulk.clearSelection')}
      >
        <X className="w-5 h-5" />
      </button>
    </div>
  );
};
