import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { itemsApi, foldersApi } from '../lib/itemsApi';
import type { ItemStatus } from '../types/items';
import { Plus, MoreVertical } from 'lucide-react';
import toast from 'react-hot-toast';

export const KanbanBoard = () => {
  const queryClient = useQueryClient();
  const [selectedFolderId, setSelectedFolderId] = useState<string | null>(null);
  const [isNoteModalOpen, setIsNoteModalOpen] = useState(false);
  const [noteForm, setNoteForm] = useState({ title: '', contentMarkdown: '' });

  // Fetch Folders
  const { data: folders = [] } = useQuery({
    queryKey: ['folders'],
    queryFn: () => foldersApi.getFolders()
  });

  // Fetch Items
  const { data: pagedItems, isLoading, isError } = useQuery({
    queryKey: ['items', selectedFolderId],
    queryFn: () => itemsApi.getItems({ 
      folderId: selectedFolderId || undefined, 
      limit: 100 
    })
  });

  const items = pagedItems?.items || [];
  if (pagedItems && pagedItems.total > 100) {
    console.warn(`Total items is ${pagedItems.total}, which exceeds the limit of 100. Some items are truncated.`);
  }

  const updateStatus = useMutation({
    mutationFn: ({ id, status }: { id: string, status: ItemStatus }) => 
      itemsApi.updateItemStatus(id, { status }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['items'] });
    },
    onError: () => toast.error('Failed to update status')
  });

  const createNote = useMutation({
    mutationFn: () => itemsApi.createNote({
      ...noteForm,
      folderId: selectedFolderId || undefined
    }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['items'] });
      setIsNoteModalOpen(false);
      setNoteForm({ title: '', contentMarkdown: '' });
      toast.success('Note created');
    },
    onError: () => toast.error('Failed to create note')
  });

  // Drag and Drop handlers
  const handleDragStart = (e: React.DragEvent, id: string) => {
    e.dataTransfer.setData('itemId', id);
  };

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
  };

  const handleDrop = (e: React.DragEvent, status: ItemStatus) => {
    e.preventDefault();
    const itemId = e.dataTransfer.getData('itemId');
    if (itemId) {
      updateStatus.mutate({ id: itemId, status });
    }
  };

  const columns: { title: string, status: ItemStatus, color: string }[] = [
    { title: 'Inbox / To Do', status: 'Inbox', color: 'bg-gray-800' },
    { title: 'In Progress', status: 'Doing', color: 'bg-blue-900/30' },
    { title: 'Done', status: 'Done', color: 'bg-emerald-900/30' },
  ];

  return (
    <div className="h-full flex flex-col md:flex-row overflow-hidden bg-[#13141f] text-gray-200">
      {/* Sidebar for Folders */}
      <div className="w-64 border-r border-gray-800 bg-[#0f1019] p-4 flex flex-col shrink-0">
        <h2 className="text-xs uppercase tracking-wider font-semibold mb-4 text-gray-500">Kanban Folders</h2>
        <ul className="space-y-1 overflow-y-auto flex-1 hide-scrollbar">
          <li>
            <button 
              className={`w-full text-left px-3 py-2 rounded-lg transition-colors text-sm font-medium ${selectedFolderId === null ? 'bg-brand-500 text-white' : 'text-gray-400 hover:bg-gray-800 hover:text-gray-200'}`}
              onClick={() => setSelectedFolderId(null)}
            >
              All Items
            </button>
          </li>
          {folders.map(f => (
            <li key={f.id}>
              <button 
                className={`w-full text-left px-3 py-2 rounded-lg transition-colors text-sm font-medium ${selectedFolderId === f.id ? 'bg-brand-500 text-white' : 'text-gray-400 hover:bg-gray-800 hover:text-gray-200'}`}
                onClick={() => setSelectedFolderId(f.id)}
              >
                <span className="mr-2">{f.icon || '📁'}</span>
                {f.name}
              </button>
            </li>
          ))}
        </ul>
      </div>

      {/* Main Board */}
      <div className="flex-1 flex flex-col min-w-0 overflow-hidden">
        <div className="px-6 py-4 border-b border-gray-800 flex justify-between items-center shrink-0">
          <h1 className="text-xl font-bold text-white">Kanban Board</h1>
          <button 
            className="flex items-center space-x-2 bg-brand-500 hover:bg-brand-600 text-white px-4 py-2 rounded-lg font-medium transition-all shadow-sm shadow-brand-500/20 hover:shadow-brand-500/40"
            onClick={() => setIsNoteModalOpen(true)}
          >
            <Plus className="w-4 h-4" />
            <span>New Note</span>
          </button>
        </div>

        {pagedItems && pagedItems.total > 100 && (
          <div className="bg-yellow-950/20 border-b border-yellow-800/40 px-6 py-2 text-xs text-yellow-400 font-medium flex items-center justify-between shrink-0">
            <span>⚠️ Note: Only the first 100 items are displayed. Some items may be truncated.</span>
          </div>
        )}

        <div className="flex-1 overflow-x-auto p-6">
          {isError ? (
            <div className="h-full flex flex-col items-center justify-center border border-dashed border-red-800/30 rounded-2xl bg-red-950/10 p-8 text-center max-w-md mx-auto my-12">
              <span className="text-red-400 text-sm font-semibold mb-2">Failed to load items</span>
              <p className="text-xs text-gray-500">There was an error retrieving the items. Please try refreshing or try again later.</p>
            </div>
          ) : (
            <div className="flex h-full gap-6 min-w-[900px]">
              {columns.map(col => (
                <div 
                  key={col.status} 
                  className={`flex-1 rounded-2xl p-4 flex flex-col border border-gray-800/50 ${col.color} backdrop-blur-sm`}
                  onDragOver={handleDragOver}
                  onDrop={(e) => handleDrop(e, col.status)}
                >
                  <div className="flex justify-between items-center mb-4 px-2">
                    <h3 className="font-semibold text-gray-100">{col.title}</h3>
                    <span className="bg-[#0f1019] text-gray-400 text-xs px-2.5 py-1 rounded-full font-medium shadow-inner">
                      {items.filter(i => i.status === col.status).length}
                    </span>
                  </div>

                  <div className="flex-1 overflow-y-auto space-y-3 hide-scrollbar pb-4">
                    {items.filter(i => i.status === col.status).map(item => (
                      <div 
                        key={item.id} 
                        draggable={!updateStatus.isPending}
                        onDragStart={(e) => handleDragStart(e, item.id)}
                        className="bg-[#1c1d2c] border border-gray-700/50 p-4 rounded-xl cursor-grab active:cursor-grabbing hover:border-brand-500/50 transition-all group relative shadow-sm"
                      >
                        <div className="flex justify-between items-start mb-2.5">
                          <div className="flex space-x-2">
                            <span className="text-[10px] uppercase font-bold tracking-widest text-brand-400 bg-brand-400/10 px-2 py-0.5 rounded border border-brand-400/20">
                              {item.type}
                            </span>
                            {item.isImportant && (
                              <span className="text-[10px] uppercase font-bold tracking-widest text-red-400 bg-red-400/10 px-2 py-0.5 rounded border border-red-400/20">
                                High
                              </span>
                            )}
                          </div>
                          <button className="text-gray-500 hover:text-white opacity-0 group-hover:opacity-100 transition-opacity">
                            <MoreVertical className="w-4 h-4" />
                          </button>
                        </div>
                        <h4 className="text-sm font-semibold text-gray-100 mb-1.5 leading-snug">{item.title}</h4>
                        <p className="text-xs text-gray-400 line-clamp-2 leading-relaxed">
                          {item.snippet}
                        </p>
                      </div>
                    ))}
                    {isLoading && <div className="text-center text-gray-500 py-6 text-sm font-medium animate-pulse">Loading items...</div>}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Create Note Modal */}
      {isNoteModalOpen && (
        <div className="fixed inset-0 bg-[#0f1019]/80 backdrop-blur-sm flex items-center justify-center p-4 z-50">
          <div className="bg-[#1c1d2c] rounded-2xl border border-gray-700/50 w-full max-w-lg overflow-hidden shadow-2xl">
            <div className="p-5 border-b border-gray-800 flex justify-between items-center">
              <h2 className="text-lg font-semibold text-white">Create New Note</h2>
              <button 
                onClick={() => setIsNoteModalOpen(false)}
                className="text-gray-500 hover:text-white transition-colors text-2xl leading-none w-8 h-8 flex items-center justify-center rounded-lg hover:bg-gray-800"
              >
                &times;
              </button>
            </div>
            <div className="p-6 space-y-5">
              <div>
                <label className="block text-sm font-medium text-gray-400 mb-1.5">Title</label>
                <input 
                  type="text" 
                  value={noteForm.title}
                  onChange={e => setNoteForm({...noteForm, title: e.target.value})}
                  className="w-full bg-[#0f1019] border border-gray-700 rounded-lg py-2.5 px-3 text-gray-100 focus:outline-none focus:border-brand-500 focus:ring-1 focus:ring-brand-500 transition-colors placeholder-gray-600"
                  placeholder="Give your note a title..."
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-400 mb-1.5">Content (Markdown)</label>
                <textarea 
                  value={noteForm.contentMarkdown}
                  onChange={e => setNoteForm({...noteForm, contentMarkdown: e.target.value})}
                  className="w-full h-32 bg-[#0f1019] border border-gray-700 rounded-lg py-2.5 px-3 text-gray-100 focus:outline-none focus:border-brand-500 focus:ring-1 focus:ring-brand-500 transition-colors resize-none placeholder-gray-600 font-mono text-sm leading-relaxed"
                  placeholder="Write your content here..."
                ></textarea>
              </div>
            </div>
            <div className="p-5 border-t border-gray-800 flex justify-end space-x-3 bg-[#13141f]">
              <button 
                onClick={() => setIsNoteModalOpen(false)}
                className="px-4 py-2 text-sm font-medium text-gray-400 hover:text-white transition-colors"
              >
                Cancel
              </button>
              <button 
                onClick={() => createNote.mutate()}
                disabled={!noteForm.title || !noteForm.contentMarkdown || createNote.isPending}
                className="bg-brand-500 hover:bg-brand-600 disabled:bg-gray-700 disabled:text-gray-500 text-white px-5 py-2 rounded-lg font-medium transition-all"
              >
                {createNote.isPending ? 'Creating...' : 'Create Note'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
