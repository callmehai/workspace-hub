import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { FolderPlus, FolderMinus, X } from 'lucide-react';
import { foldersApi } from '../lib/itemsApi';
import { handleApiError } from '../lib/errorUtils';
import { type FolderResponse } from '../types/items';
import { useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';

interface BulkActionBarProps {
  selectedItemIds: Set<string>;
  onClearSelection: () => void;
}

export const BulkActionBar: React.FC<BulkActionBarProps> = ({ selectedItemIds, onClearSelection }) => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [isAdding, setIsAdding] = useState(false);
  const [isRemoving, setIsRemoving] = useState(false);

  const { data: folders = [] } = useQuery({
    queryKey: ['folders'],
    queryFn: () => foldersApi.getFolders()
  });

  const addBulkMutation = useMutation({
    mutationFn: (folderId: string) => foldersApi.addItemsToFolderBulk(folderId, Array.from(selectedItemIds)),
    onSuccess: () => {
      toast.success(`Đã thêm ${selectedItemIds.size} thẻ vào thư mục`);
      queryClient.invalidateQueries({ queryKey: ['items'] });
      selectedItemIds.forEach(id => {
        queryClient.invalidateQueries({ queryKey: ['item', id] });
      });
      onClearSelection();
    },
    onError: (err) => handleApiError(err, 'Lỗi thêm vào thư mục', { navigate })
  });

  const removeBulkMutation = useMutation({
    mutationFn: (folderId: string) => foldersApi.removeItemsFromFolderBulk(folderId, Array.from(selectedItemIds)),
    onSuccess: () => {
      toast.success(`Đã gỡ ${selectedItemIds.size} thẻ khỏi thư mục`);
      queryClient.invalidateQueries({ queryKey: ['items'] });
      selectedItemIds.forEach(id => {
        queryClient.invalidateQueries({ queryKey: ['item', id] });
      });
      onClearSelection();
    },
    onError: (err) => handleApiError(err, 'Lỗi gỡ khỏi thư mục', { navigate })
  });

  if (selectedItemIds.size === 0) return null;

  return (
    <div className="fixed bottom-6 left-1/2 -translate-x-1/2 z-[100] bg-white border-2 border-slate-300 text-slate-700 px-5 py-3 rounded-2xl shadow-[0_10px_35px_-5px_rgba(0,0,0,0.15)] flex items-center gap-6 animate-in slide-in-from-bottom-10 fade-in duration-300">
      <div className="flex items-center gap-3">
        <span className="flex items-center justify-center bg-indigo-600 text-white font-bold w-6 h-6 rounded-full text-xs">
          {selectedItemIds.size}
        </span>
        <span className="text-[13.5px] font-semibold text-slate-600">đã chọn</span>
      </div>

      <div className="h-5 w-[1px] bg-slate-200"></div>

      <div className="flex items-center gap-2">
        {/* Add to folder */}
        <div className="relative">
          <button 
            onClick={() => { setIsAdding(!isAdding); setIsRemoving(false); }}
            className={`flex items-center gap-2 text-[13px] font-semibold px-3 py-1.5 rounded-lg transition-all ${
              isAdding 
                ? 'bg-indigo-50 text-indigo-600' 
                : 'text-slate-600 hover:bg-slate-50 hover:text-slate-900'
            }`}
          >
            <FolderPlus className="w-4 h-4" />
            Thêm vào...
          </button>
          
          {isAdding && (
            <div className="absolute bottom-full left-0 mb-2 w-56 bg-white border border-slate-200 shadow-xl rounded-xl py-1.5 text-slate-800">
              <div className="px-3 py-2 text-[11px] font-semibold text-slate-400 uppercase tracking-wider">Thêm vào thư mục</div>
              <div className="max-h-60 overflow-y-auto">
                {folders.length === 0 ? (
                  <div className="px-4 py-2 text-sm text-slate-500">Chưa có thư mục</div>
                ) : (
                  folders.map((f: FolderResponse) => (
                    <button
                      key={f.id}
                      disabled={addBulkMutation.isPending}
                      onClick={() => addBulkMutation.mutate(f.id)}
                      className="w-full text-left px-4 py-2 text-[13px] font-medium hover:bg-slate-50 flex items-center gap-2.5 transition-colors"
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
                ? 'bg-rose-50 text-rose-600' 
                : 'text-slate-600 hover:bg-slate-50 hover:text-slate-900'
            }`}
          >
            <FolderMinus className="w-4 h-4" />
            Gỡ khỏi...
          </button>
          
          {isRemoving && (
            <div className="absolute bottom-full left-0 mb-2 w-56 bg-white border border-slate-200 shadow-xl rounded-xl py-1.5 text-slate-800">
              <div className="px-3 py-2 text-[11px] font-semibold text-slate-400 uppercase tracking-wider">Gỡ khỏi thư mục</div>
              <div className="max-h-60 overflow-y-auto">
                {folders.length === 0 ? (
                  <div className="px-4 py-2 text-sm text-slate-500">Chưa có thư mục</div>
                ) : (
                  folders.map((f: FolderResponse) => (
                    <button
                      key={f.id}
                      disabled={removeBulkMutation.isPending}
                      onClick={() => removeBulkMutation.mutate(f.id)}
                      className="w-full text-left px-4 py-2 text-[13px] font-medium hover:bg-rose-50 hover:text-rose-600 flex items-center gap-2.5 transition-colors"
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
      </div>

      <div className="h-5 w-[1px] bg-slate-200"></div>

      <button 
        onClick={onClearSelection}
        className="p-1.5 hover:bg-slate-100 rounded-full text-slate-400 hover:text-slate-600 transition-colors"
        title="Hủy chọn"
      >
        <X className="w-5 h-5" />
      </button>
    </div>
  );
};
