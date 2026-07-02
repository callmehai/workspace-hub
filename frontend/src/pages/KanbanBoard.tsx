import React, { useState, useEffect, useRef, useCallback } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useLocation, Link } from 'react-router-dom';
import { itemsApi, foldersApi } from '../lib/itemsApi';
import { connectionsApi, type ConnectionDto } from '../lib/connectionsApi';
import { ItemDetail } from '../components/ItemDetail';
import { BulkActionBar } from '../components/BulkActionBar';
import type { ItemStatus, ItemType, FolderResponse, ItemResponse, PagedResult } from '../types/items';
import {
  Plus, Loader2, Mail, Calendar, FileText, StickyNote, Briefcase,
  Star, GripVertical, AlertCircle, LayoutGrid, List as ListIcon, Search
} from 'lucide-react';
import toast from 'react-hot-toast';
import { handleApiError } from '../lib/errorUtils';
import { formatDistanceToNow } from 'date-fns';
import { vi } from 'date-fns/locale';

function formatTime(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return '';
  return formatDistanceToNow(d, { addSuffix: true, locale: vi });
}

function typeLabel(t: ItemType): string {
  const map: Record<ItemType, string> = {
    Email: 'Email', Event: 'Sự kiện', File: 'Tệp', Note: 'Ghi chú', Ticket: 'Ticket',
  };
  return map[t] ?? t;
}

function typeIcon(t: ItemType) {
  const cls = 'w-3.5 h-3.5';
  switch (t) {
    case 'Email': return <Mail className={cls} />;
    case 'Event': return <Calendar className={cls} />;
    case 'File': return <FileText className={cls} />;
    case 'Note': return <StickyNote className={cls} />;
    case 'Ticket': return <Briefcase className={cls} />;
  }
}

function typeTileClass(t: ItemType): string {
  const map: Record<ItemType, string> = {
    Email: 'bg-blue-50 text-blue-700',
    Event: 'bg-amber-50 text-amber-700',
    File: 'bg-emerald-50 text-emerald-700',
    Note: 'bg-slate-100 text-slate-600',
    Ticket: 'bg-violet-50 text-violet-700',
  };
  return map[t] ?? 'bg-gray-100 text-gray-700';
}

const COLUMNS: { title: string, status: ItemStatus, dotColor: string }[] = [
  { title: 'Cần xem', status: 'Inbox', dotColor: 'bg-slate-400' },
  { title: 'Đang xử lý', status: 'Doing', dotColor: 'bg-blue-500' },
  { title: 'Done', status: 'Done', dotColor: 'bg-emerald-500' },
];

interface ChipProps {
  active: boolean;
  onClick: () => void;
  children: React.ReactNode;
}
function Chip({ active, onClick, children }: ChipProps) {
  return (
    <button
      onClick={onClick}
      className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-[13px] font-medium border transition-colors whitespace-nowrap
        ${active
          ? 'bg-indigo-50 text-indigo-700 border-indigo-200'
          : 'bg-white text-slate-600 border-slate-200 hover:border-slate-300'
        }`}
    >
      {children}
    </button>
  );
}

const TYPE_FILTERS: { label: string; value: ItemType | null }[] = [
  { label: 'Tất cả', value: null },
  { label: 'Email', value: 'Email' },
  { label: 'Sự kiện', value: 'Event' },
  { label: 'Tệp', value: 'File' },
  { label: 'Ghi chú', value: 'Note' },
  { label: 'Ticket', value: 'Ticket' },
];

export const KanbanBoard = () => {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const location = useLocation();

  const [selectedFolderId, setSelectedFolderId] = useState<string | null>(null);
  const [selectedItemId, setSelectedItemId] = useState<string | null>(null);

  const [search, setSearch] = useState('');
  const [searchInput, setSearchInput] = useState('');
  const [typeFilter, setTypeFilter] = useState<ItemType | null>('Ticket');
  const [importantOnly, setImportantOnly] = useState(false);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const [selectedItemIds, setSelectedItemIds] = useState<Set<string>>(new Set());

  const handleSearchChange = useCallback((val: string) => {
    setSearchInput(val);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setSearch(val.trim());
    }, 350);
  }, []);

  const [isNoteModalOpen, setIsNoteModalOpen] = useState(false);
  const [noteForm, setNoteForm] = useState({ title: '', contentMarkdown: '' });
  
  const [addingFolderItemId, setAddingFolderItemId] = useState<string | null>(null);

  const isEventModalOpenState = useState(false);
  const [isEventModalOpen, setIsEventModalOpen] = isEventModalOpenState;
  const [eventForm, setEventForm] = useState({
    connectionId: '', title: '', start: '', end: '', location: '', attendees: ''
  });

  const [dragOverCol, setDragOverCol] = useState<ItemStatus | null>(null);
  const [draggingId, setDraggingId] = useState<string | null>(null);

  useEffect(() => {
    const params = new URLSearchParams(location.search);
    const folder = params.get('folder');
    if (folder) setSelectedFolderId(folder);
    else setSelectedFolderId(null);
  }, [location.search]);

  const { data: folders = [] } = useQuery({
    queryKey: ['folders'],
    queryFn: () => foldersApi.getFolders()
  });

  const queryKey = ['items', { folderId: selectedFolderId, type: typeFilter, isImportant: importantOnly, search }];
  const { data: pagedItems, isLoading, isError, refetch } = useQuery({
    queryKey,
    queryFn: () => itemsApi.getItems({ 
      folderId: selectedFolderId || undefined, 
      type: typeFilter || undefined,
      isImportant: importantOnly || undefined,
      search: search || undefined,
      limit: 100 
    })
  });

  const items = pagedItems?.items || [];

  const { data: connections = [] } = useQuery({
    queryKey: ['connections'],
    queryFn: () => connectionsApi.getConnections(),
    enabled: isEventModalOpen
  });
  const gcalConnections = connections.filter(
    (c: ConnectionDto) => c.serviceType.toLowerCase() === 'gcal' && c.status.toLowerCase() === 'active'
  );

  const updateStatus = useMutation({
    mutationFn: ({ id, status, type }: { id: string, status: ItemStatus, type: ItemType }) => {
      if (type === 'Ticket') {
        const transitionMap: Record<ItemStatus, string> = {
          Inbox: 'To Do',
          Doing: 'In Progress',
          Done: 'Done'
        };
        return itemsApi.patchItem(id, { statusTransition: transitionMap[status] });
      }
      return itemsApi.updateItemStatus(id, { status });
    },
    onMutate: async ({ id, status }) => {
      await queryClient.cancelQueries({ queryKey });
      const previous = queryClient.getQueryData<PagedResult<ItemResponse>>(queryKey);
      queryClient.setQueryData(queryKey, (old: PagedResult<ItemResponse> | undefined) => {
        if (!old) return old;
        return {
          ...old,
          items: old.items.map((it: ItemResponse) => it.id === id ? { ...it, status } : it)
        };
      });
      return { previous };
    },
    onError: (err, _vars, ctx) => {
      if (ctx?.previous) queryClient.setQueryData(queryKey, ctx.previous);
      handleApiError(err, 'Lỗi cập nhật trạng thái', { navigate });
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ['items'] });
    }
  });

  const createNote = useMutation({
    mutationFn: () => itemsApi.createNote({ ...noteForm, folderId: selectedFolderId || undefined }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['items'] });
      setIsNoteModalOpen(false);
      setNoteForm({ title: '', contentMarkdown: '' });
      toast.success('Đã tạo ghi chú');
    },
    onError: (err) => handleApiError(err, 'Lỗi tạo ghi chú', { navigate })
  });

  const createEvent = useMutation({
    mutationFn: () => {
      if (!eventForm.connectionId) throw new Error('Vui lòng chọn tài khoản');
      const startIso = new Date(eventForm.start).toISOString();
      const endIso = new Date(eventForm.end).toISOString();
      const attendeesArray = eventForm.attendees
        ? eventForm.attendees.split(',').map(e => e.trim()).filter(e => e.length > 0)
        : undefined;
      return itemsApi.createEvent({
        connectionId: eventForm.connectionId, title: eventForm.title,
        start: startIso, end: endIso, location: eventForm.location || undefined, attendees: attendeesArray
      });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['items'] });
      setIsEventModalOpen(false);
      setEventForm({ connectionId: '', title: '', start: '', end: '', location: '', attendees: '' });
      toast.success('Đã tạo sự kiện');
    },
    onError: (err) => handleApiError(err, 'Lỗi tạo sự kiện', { navigate })
  });

  const addToFolderMutation = useMutation({
    mutationFn: ({ folderId, itemId }: { folderId: string, itemId: string }) => foldersApi.addItemToFolder(folderId, { itemId }),
    onSuccess: () => {
      toast.success('Đã thêm vào thư mục');
      queryClient.invalidateQueries({ queryKey: ['items'] });
    },
    onError: (err) => handleApiError(err, 'Lỗi thêm vào thư mục', { navigate })
  });

  const handleCreateEvent = () => {
    if (!eventForm.connectionId || !eventForm.title || !eventForm.start || !eventForm.end) {
      toast.error('Vui lòng điền đủ thông tin');
      return;
    }
    const startIso = new Date(eventForm.start).toISOString();
    const endIso = new Date(eventForm.end).toISOString();
    if (new Date(startIso) >= new Date(endIso)) {
      toast.error('Bắt đầu phải trước kết thúc');
      return;
    }

    if (eventForm.attendees) {
      const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
      const attendeesArray = eventForm.attendees.split(',').map(e => e.trim()).filter(e => e.length > 0);
      const invalidEmails = attendeesArray.filter(email => !emailRegex.test(email));
      if (invalidEmails.length > 0) {
        toast.error(`Email không hợp lệ: ${invalidEmails.join(', ')}`);
        return;
      }
    }

    createEvent.mutate();
  };

  const handleDragStart = (e: React.DragEvent, id: string) => {
    setDraggingId(id);
    if (selectedItemIds.has(id) && selectedItemIds.size > 1) {
      e.dataTransfer.setData('itemIds', JSON.stringify(Array.from(selectedItemIds)));
    } else {
      e.dataTransfer.setData('itemId', id);
    }
  };

  const handleDragEnd = () => {
    setDraggingId(null);
    setDragOverCol(null);
  };

  const handleDragOver = (e: React.DragEvent, status: ItemStatus) => {
    e.preventDefault();
    if (dragOverCol !== status) setDragOverCol(status);
  };

  const handleDragLeave = () => {
    setDragOverCol(null);
  };

  const handleDrop = (e: React.DragEvent, status: ItemStatus) => {
    e.preventDefault();
    setDragOverCol(null);
    setDraggingId(null);
    const itemId = e.dataTransfer.getData('itemId');
    const itemIds = e.dataTransfer.getData('itemIds');
    
    if (itemIds) {
      const ids = JSON.parse(itemIds);
      ids.forEach((id: string) => {
        const item = items.find((i: ItemResponse) => i.id === id);
        if (item) updateStatus.mutate({ id, status, type: item.type });
      });
    } else if (itemId) {
      const draggedItem = items.find((i: ItemResponse) => i.id === itemId);
      if (draggedItem) {
        updateStatus.mutate({ id: itemId, status, type: draggedItem.type });
      }
    }
  };

  const currentFolderName = selectedFolderId 
    ? (folders.find((f: FolderResponse) => f.id === selectedFolderId)?.name || 'Thư mục ẩn')
    : 'Tất cả thư mục';

  return (
    <div className="h-full flex flex-col min-w-0 overflow-hidden bg-slate-50 text-slate-900">
      {/* Header & Filters */}
      <div className="px-6 py-4 border-b border-slate-200 bg-white flex flex-col shrink-0 gap-4">
        <div className="flex justify-between items-end flex-wrap gap-4">
          <div>
            <h1 className="text-[22px] font-semibold text-slate-900 leading-tight mb-0.5">Bảng Kanban</h1>
            <p className="text-[13px] text-slate-500">{currentFolderName} · kéo-thả thẻ để đổi trạng thái</p>
          </div>
          <div className="flex items-center gap-3">
            <div className="flex items-center gap-1 p-1 bg-slate-100 border border-slate-200 rounded-lg">
              <Link
                to={selectedFolderId ? `/?folder=${selectedFolderId}` : '/'}
                className="flex items-center gap-1.5 px-3 py-1.5 text-[13px] font-medium text-slate-600 rounded-md hover:text-slate-900 hover:bg-white transition-colors"
              >
                <ListIcon className="w-4 h-4" /> Danh sách
              </Link>
              <div className="flex items-center gap-1.5 px-3 py-1.5 text-[13px] font-semibold text-indigo-700 bg-white shadow-sm rounded-md border border-slate-200/50">
                <LayoutGrid className="w-4 h-4" /> Bảng
              </div>
            </div>
            <div className="flex gap-2 ml-2 border-l border-slate-200 pl-4">
              <button
                onClick={() => setIsNoteModalOpen(true)}
                className="inline-flex items-center justify-center gap-1.5 px-3 py-1.5 text-[13px] font-medium text-slate-700 bg-white border border-slate-300 rounded-lg shadow-sm hover:bg-slate-50 transition-all"
              >
                <Plus className="w-4 h-4" /> Ghi chú
              </button>
              <button
                onClick={() => setIsEventModalOpen(true)}
                className="inline-flex items-center justify-center gap-1.5 px-3 py-1.5 text-[13px] font-medium text-slate-700 bg-white border border-slate-300 rounded-lg shadow-sm hover:bg-slate-50 transition-all"
              >
                <Plus className="w-4 h-4" /> Sự kiện
              </button>
            </div>
          </div>
        </div>

        {/* Filter bar */}
        <div className="flex flex-wrap gap-2 items-center">
          {TYPE_FILTERS.map(f => (
            <Chip key={String(f.value)} active={typeFilter === f.value} onClick={() => setTypeFilter(f.value)}>
              {f.value ? (
                <span className={`inline-flex items-center gap-1 ${typeTileClass(f.value)} px-0 bg-transparent`}>
                  {typeIcon(f.value)}{f.label}
                </span>
              ) : f.label}
            </Chip>
          ))}
          <div className="w-px h-[22px] bg-slate-200 mx-0.5" />
          <Chip active={importantOnly} onClick={() => setImportantOnly(v => !v)}>
            <Star className={`w-3.5 h-3.5 ${importantOnly ? 'fill-amber-400 text-amber-400' : 'text-slate-400'}`} />
            Quan trọng
          </Chip>
          <div className="relative ml-auto">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
            <input
              type="text"
              value={searchInput}
              onChange={e => handleSearchChange(e.target.value)}
              placeholder="Tìm kiếm..."
              className="w-64 h-8 pl-9 pr-4 rounded-lg border border-slate-200 bg-slate-50 text-[13px] text-slate-900 placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500/30 focus:border-indigo-400 transition"
            />
          </div>
        </div>

        {pagedItems && pagedItems.total > 100 && (
          <div className="mt-3 p-3 bg-amber-50 border border-amber-200 rounded-lg flex items-center gap-2 text-amber-800 text-[13px] font-medium shrink-0 animate-in fade-in duration-200">
            <AlertCircle className="w-4 h-4 text-amber-500 shrink-0" />
            <span>Chỉ hiển thị tối đa 100 mục đầu tiên trên bảng Kanban. Thư mục hiện tại có {pagedItems.total} mục.</span>
          </div>
        )}
      </div>

      {/* Board content */}
      <div className="flex-1 overflow-x-auto overflow-y-hidden p-6 bg-slate-50">
          {isError ? (
            <div className="h-full flex flex-col items-center justify-center border-2 border-dashed border-slate-200 rounded-xl bg-white p-8 text-center max-w-md mx-auto">
              <div className="w-12 h-12 rounded-xl bg-red-50 text-red-500 flex items-center justify-center mb-4">
                <AlertCircle className="w-6 h-6" />
              </div>
              <span className="text-slate-900 text-sm font-semibold mb-1">Không tải được bảng</span>
              <p className="text-[13px] text-slate-500 mb-4">Mất kết nối tới máy chủ. Vui lòng thử lại.</p>
              <button onClick={() => refetch()} className="px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white text-[13px] font-medium rounded-lg">
                Thử lại
              </button>
            </div>
          ) : (
            <div className="flex h-full gap-5 min-w-[900px]">
              {COLUMNS.map(col => {
                const colItems = items.filter(i => i.status === col.status);
                const isOver = dragOverCol === col.status;
                return (
                  <div key={col.status} className="flex-1 w-80 flex flex-col min-h-0">
                    <div className="flex items-center gap-2 px-1 mb-2">
                      <span className={`w-2 h-2 rounded-full ${col.dotColor}`}></span>
                      <span className="text-[14px] font-semibold text-slate-900">{col.title}</span>
                      <span className="text-[12px] font-semibold text-slate-500 bg-slate-200 px-2 py-0.5 rounded-full">
                        {colItems.length}
                      </span>
                    </div>

                    <div
                      onDragOver={(e) => handleDragOver(e, col.status)}
                      onDragLeave={handleDragLeave}
                      onDrop={(e) => handleDrop(e, col.status)}
                      className={`flex-1 flex flex-col gap-2.5 p-2.5 rounded-xl min-h-[160px] overflow-y-auto transition-colors border-2 ${
                        isOver ? 'bg-indigo-50 border-indigo-400 border-dashed' : 'bg-slate-100/80 border-transparent'
                      }`}
                    >
                      {isLoading ? (
                        Array.from({ length: 3 }).map((_, i) => (
                          <div key={i} className="bg-white border border-slate-200 rounded-xl p-3 animate-pulse">
                            <div className="h-4 bg-slate-200 rounded w-1/4 mb-3"></div>
                            <div className="h-3 bg-slate-200 rounded w-3/4 mb-2"></div>
                            <div className="h-3 bg-slate-200 rounded w-1/2"></div>
                          </div>
                        ))
                      ) : (
                        colItems.map(item => (
                          <div
                            key={item.id}
                            draggable={!(updateStatus.isPending && updateStatus.variables?.id === item.id)}
                            onDragStart={(e) => handleDragStart(e, item.id)}
                            onDragEnd={handleDragEnd}
                            onClick={() => setSelectedItemId(item.id)}
                            className={`bg-white border rounded-xl p-3 cursor-pointer group hover:shadow-md hover:border-slate-300 transition-all ${
                              draggingId === item.id ? 'opacity-40 shadow-none border-slate-200' : 'opacity-100 shadow-sm border-slate-200'
                            }`}
                          >
                            <div className="flex justify-between items-center mb-2">
                              <div className="flex items-center gap-1.5 flex-wrap">
                                <input 
                                  type="checkbox" 
                                  checked={selectedItemIds.has(item.id)}
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    const newSet = new Set(selectedItemIds);
                                    if (newSet.has(item.id)) newSet.delete(item.id);
                                    else newSet.add(item.id);
                                    setSelectedItemIds(newSet);
                                  }}
                                  onChange={() => {}}
                                  className={`w-3.5 h-3.5 rounded border-slate-300 text-indigo-600 focus:ring-indigo-600 ${selectedItemIds.has(item.id) ? 'opacity-100' : 'opacity-0 group-hover:opacity-100'} transition-opacity`}
                                />
                                <span className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md text-[11px] font-medium border border-transparent ${typeTileClass(item.type)}`}>
                                  {typeIcon(item.type)}
                                  {typeLabel(item.type)}
                                </span>
                                {item.folderIds?.map(fId => {
                                  const f = folders.find(fol => fol.id === fId);
                                  if (!f) return null;
                                  return (
                                    <span key={f.id} className="inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-medium border border-slate-100" style={{ backgroundColor: f.color ? `${f.color}15` : '#f1f5f9', color: f.color || '#475569' }}>
                                      {f.name}
                                    </span>
                                  );
                                })}
                                
                                <div className="relative" onClick={e => e.stopPropagation()}>
                                  <button
                                    onClick={(e) => {
                                      e.stopPropagation();
                                      setAddingFolderItemId(addingFolderItemId === item.id ? null : item.id);
                                    }}
                                    className="inline-flex items-center justify-center gap-0.5 px-1.5 py-0.5 rounded text-[10px] font-medium border border-slate-200 bg-white text-slate-500 hover:text-slate-700 hover:bg-slate-50 transition-colors opacity-0 group-hover:opacity-100"
                                    title="Thêm thư mục"
                                  >
                                    <Plus className="w-2.5 h-2.5" />
                                    <span>Thêm</span>
                                  </button>
                                  
                                  {addingFolderItemId === item.id && (
                                    <div className="absolute top-full left-0 mt-1 w-44 bg-white border border-slate-200 shadow-xl rounded-md py-1 z-[60] animate-in fade-in zoom-in-95 duration-100">
                                      {folders.filter((f: any) => !item.folderIds?.includes(f.id)).length === 0 ? (
                                        <div className="px-3 py-1.5 text-[11px] text-slate-500 text-center">Không còn thư mục</div>
                                      ) : (
                                        folders.filter((f: any) => !item.folderIds?.includes(f.id)).map((f: any) => (
                                          <button
                                            key={f.id}
                                            onClick={(e) => {
                                              e.stopPropagation();
                                              addToFolderMutation.mutate({ folderId: f.id, itemId: item.id });
                                              setAddingFolderItemId(null);
                                            }}
                                            disabled={addToFolderMutation.isPending}
                                            className="w-full text-left px-3 py-1.5 text-[11.5px] font-medium text-slate-700 hover:bg-slate-50 flex items-center gap-2 transition-colors"
                                          >
                                            <span className="w-1.5 h-1.5 rounded-full shrink-0" style={{ backgroundColor: f.color || '#f59e0b' }}></span>
                                            <span className="truncate">{f.name}</span>
                                          </button>
                                        ))
                                      )}
                                    </div>
                                  )}
                                </div>
                              </div>
                              <span className="text-slate-300 cursor-grab active:cursor-grabbing hover:text-slate-400">
                                <GripVertical className="w-4 h-4" />
                              </span>
                            </div>
                            <h4 className="text-[13.5px] font-medium text-slate-900 leading-snug mb-2 line-clamp-2">
                              {item.title}
                            </h4>
                            <div className="flex items-center justify-end gap-2 mt-auto pt-1">
                              <span className="inline-flex items-center gap-1.5 text-[12px] text-slate-400 shrink-0">
                                {item.isImportant && <Star className="w-3.5 h-3.5 fill-amber-400 text-amber-400" />}
                                {formatTime(item.occurredAt)}
                              </span>
                            </div>
                          </div>
                        ))
                      )}

                      {!isLoading && colItems.length === 0 && (
                        <div className="flex items-center justify-center p-4 border-[1.5px] border-dashed border-slate-300 rounded-xl text-[12.5px] text-slate-400 text-center h-20">
                          Kéo thẻ vào đây
                        </div>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>

      {/* Note Modal */}
      {isNoteModalOpen && (
        <div className="fixed inset-0 bg-slate-900/40 backdrop-blur-sm flex items-center justify-center p-4 z-50">
          <div className="bg-white rounded-xl w-full max-w-lg shadow-xl overflow-hidden flex flex-col">
            <div className="px-5 py-4 border-b border-slate-200 flex justify-between items-center">
              <h2 className="text-[16px] font-semibold text-slate-900">Tạo ghi chú mới</h2>
              <button onClick={() => setIsNoteModalOpen(false)} className="text-slate-400 hover:text-slate-600 hover:bg-slate-100 p-1 rounded-lg transition-colors">
                &times;
              </button>
            </div>
            <div className="p-5 space-y-4">
              <div>
                <label className="block text-[13px] font-medium text-slate-700 mb-1.5">Tiêu đề</label>
                <input
                  type="text"
                  value={noteForm.title}
                  onChange={e => setNoteForm({ ...noteForm, title: e.target.value })}
                  className="w-full bg-white border border-slate-300 rounded-lg py-2 px-3 text-[13px] text-slate-900 focus:outline-none focus:ring-2 focus:ring-indigo-500/20 focus:border-indigo-500 transition-shadow"
                  placeholder="Tiêu đề ghi chú..."
                />
              </div>
              <div>
                <label className="block text-[13px] font-medium text-slate-700 mb-1.5">Nội dung (Markdown)</label>
                <textarea
                  value={noteForm.contentMarkdown}
                  onChange={e => setNoteForm({ ...noteForm, contentMarkdown: e.target.value })}
                  className="w-full h-32 bg-white border border-slate-300 rounded-lg py-2 px-3 text-[13px] text-slate-900 focus:outline-none focus:ring-2 focus:ring-indigo-500/20 focus:border-indigo-500 transition-shadow resize-none"
                  placeholder="Nội dung ghi chú..."
                ></textarea>
              </div>
            </div>
            <div className="px-5 py-4 border-t border-slate-200 bg-slate-50 flex justify-end gap-2">
              <button onClick={() => setIsNoteModalOpen(false)} className="px-4 py-2 text-[13px] font-medium text-slate-600 hover:text-slate-900 hover:bg-slate-200 rounded-lg transition-colors">
                Hủy
              </button>
              <button
                onClick={() => createNote.mutate()}
                disabled={!noteForm.title || !noteForm.contentMarkdown || createNote.isPending}
                className="bg-indigo-600 hover:bg-indigo-700 disabled:bg-indigo-300 text-white px-4 py-2 rounded-lg text-[13px] font-medium transition-colors inline-flex items-center gap-2"
              >
                {createNote.isPending && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
                {createNote.isPending ? 'Đang tạo...' : 'Tạo ghi chú'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Event Modal */}
      {isEventModalOpen && (
        <div className="fixed inset-0 bg-slate-900/40 backdrop-blur-sm flex items-center justify-center p-4 z-50">
          <div className="bg-white rounded-xl w-full max-w-lg shadow-xl overflow-hidden flex flex-col">
            <div className="px-5 py-4 border-b border-slate-200 flex justify-between items-center">
              <h2 className="text-[16px] font-semibold text-slate-900">Tạo sự kiện Calendar mới</h2>
              <button onClick={() => setIsEventModalOpen(false)} className="text-slate-400 hover:text-slate-600 hover:bg-slate-100 p-1 rounded-lg transition-colors">
                &times;
              </button>
            </div>
            <div className="p-5 space-y-4 max-h-[70vh] overflow-y-auto">
              <div>
                <label className="block text-[13px] font-medium text-slate-700 mb-1.5">Tài khoản Google Calendar</label>
                <select
                  value={eventForm.connectionId}
                  onChange={e => setEventForm({ ...eventForm, connectionId: e.target.value })}
                  className="w-full bg-white border border-slate-300 rounded-lg py-2 px-3 text-[13px] text-slate-900 focus:outline-none focus:ring-2 focus:ring-indigo-500/20 focus:border-indigo-500 transition-shadow"
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
                <input
                  type="text"
                  value={eventForm.title}
                  onChange={e => setEventForm({ ...eventForm, title: e.target.value })}
                  className="w-full bg-white border border-slate-300 rounded-lg py-2 px-3 text-[13px] text-slate-900 focus:outline-none focus:ring-2 focus:ring-indigo-500/20 focus:border-indigo-500 transition-shadow"
                />
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-[13px] font-medium text-slate-700 mb-1.5">Bắt đầu</label>
                  <input
                    type="datetime-local"
                    value={eventForm.start}
                    onChange={e => setEventForm({ ...eventForm, start: e.target.value })}
                    className="w-full bg-white border border-slate-300 rounded-lg py-2 px-3 text-[13px] text-slate-900 focus:outline-none focus:ring-2 focus:ring-indigo-500/20 focus:border-indigo-500 transition-shadow"
                  />
                </div>
                <div>
                  <label className="block text-[13px] font-medium text-slate-700 mb-1.5">Kết thúc</label>
                  <input
                    type="datetime-local"
                    value={eventForm.end}
                    onChange={e => setEventForm({ ...eventForm, end: e.target.value })}
                    className="w-full bg-white border border-slate-300 rounded-lg py-2 px-3 text-[13px] text-slate-900 focus:outline-none focus:ring-2 focus:ring-indigo-500/20 focus:border-indigo-500 transition-shadow"
                  />
                </div>
              </div>
              <div>
                <label className="block text-[13px] font-medium text-slate-700 mb-1.5">Địa điểm</label>
                <input
                  type="text"
                  value={eventForm.location}
                  onChange={e => setEventForm({ ...eventForm, location: e.target.value })}
                  className="w-full bg-white border border-slate-300 rounded-lg py-2 px-3 text-[13px] text-slate-900 focus:outline-none focus:ring-2 focus:ring-indigo-500/20 focus:border-indigo-500 transition-shadow"
                />
              </div>
              <div>
                <label className="block text-[13px] font-medium text-slate-700 mb-1.5">Người tham gia (Email cách nhau bởi dấu phẩy)</label>
                <input
                  type="text"
                  value={eventForm.attendees}
                  onChange={e => setEventForm({ ...eventForm, attendees: e.target.value })}
                  className="w-full bg-white border border-slate-300 rounded-lg py-2 px-3 text-[13px] text-slate-900 focus:outline-none focus:ring-2 focus:ring-indigo-500/20 focus:border-indigo-500 transition-shadow"
                />
              </div>
            </div>
            <div className="px-5 py-4 border-t border-slate-200 bg-slate-50 flex justify-end gap-2">
              <button onClick={() => setIsEventModalOpen(false)} className="px-4 py-2 text-[13px] font-medium text-slate-600 hover:text-slate-900 hover:bg-slate-200 rounded-lg transition-colors">
                Hủy
              </button>
              <button
                onClick={handleCreateEvent}
                disabled={!eventForm.connectionId || !eventForm.title || !eventForm.start || !eventForm.end || createEvent.isPending}
                className="bg-indigo-600 hover:bg-indigo-700 disabled:bg-indigo-300 text-white px-4 py-2 rounded-lg text-[13px] font-medium transition-colors inline-flex items-center gap-2"
              >
                {createEvent.isPending && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
                {createEvent.isPending ? 'Đang tạo...' : 'Tạo sự kiện'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Item Detail */}
      {selectedItemId && (
        <div className="fixed inset-0 bg-slate-900/40 backdrop-blur-sm flex items-center justify-end z-50">
          <div className="bg-white w-full max-w-[600px] h-full shadow-2xl flex flex-col relative animate-in slide-in-from-right duration-200">
            <ItemDetail
              itemId={selectedItemId}
              onClose={() => setSelectedItemId(null)}
              onDeleted={() => setSelectedItemId(null)}
            />
          </div>
        </div>
      )}

      {/* ── Bulk Action Bar ── */}
      <BulkActionBar 
        selectedItemIds={selectedItemIds}
        onClearSelection={() => setSelectedItemIds(new Set())}
      />
    </div>
  );
};
