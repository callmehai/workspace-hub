import React, { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useLocation, Link } from 'react-router-dom';
import { itemsApi, foldersApi } from '../lib/itemsApi';
import { connectionsApi, type ConnectionDto } from '../lib/connectionsApi';
import { ItemDetail } from '../components/ItemDetail';
import type { ItemStatus, ItemType } from '../types/items';
import {
  Plus, Loader2, Mail, Calendar, FileText, StickyNote, Briefcase,
  Star, GripVertical, AlertCircle, LayoutGrid, List as ListIcon, Folder
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

export const KanbanBoard = () => {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const location = useLocation();

  const [selectedFolderId, setSelectedFolderId] = useState<string | null>(null);
  const [selectedItemId, setSelectedItemId] = useState<string | null>(null);

  const [isNoteModalOpen, setIsNoteModalOpen] = useState(false);
  const [noteForm, setNoteForm] = useState({ title: '', contentMarkdown: '' });

  const [isEventModalOpen, setIsEventModalOpen] = useState(false);
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

  const updateFolderUrl = (folderId: string | null) => {
    const params = new URLSearchParams(location.search);
    if (folderId) params.set('folder', folderId);
    else params.delete('folder');
    navigate({ search: params.toString() }, { replace: true });
  };

  const { data: folders = [] } = useQuery({
    queryKey: ['folders'],
    queryFn: () => foldersApi.getFolders()
  });

  const queryKey = ['items', selectedFolderId];
  const { data: pagedItems, isLoading, isError, refetch } = useQuery({
    queryKey,
    queryFn: () => itemsApi.getItems({ folderId: selectedFolderId || undefined, limit: 100 })
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
    mutationFn: ({ id, status }: { id: string, status: ItemStatus }) =>
      itemsApi.updateItemStatus(id, { status }),
    onMutate: async ({ id, status }) => {
      await queryClient.cancelQueries({ queryKey });
      const previous = queryClient.getQueryData<any>(queryKey);
      queryClient.setQueryData(queryKey, (old: any) => {
        if (!old) return old;
        return {
          ...old,
          items: old.items.map((it: any) => it.id === id ? { ...it, status } : it)
        };
      });
      return { previous };
    },
    onError: (err, _vars, ctx: any) => {
      if (ctx?.previous) queryClient.setQueryData(queryKey, ctx.previous);
      handleApiError(err, 'Lỗi cập nhật trạng thái', { navigate });
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey });
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
    createEvent.mutate();
  };

  const handleDragStart = (e: React.DragEvent, id: string) => {
    e.dataTransfer.setData('itemId', id);
    setDraggingId(id);
    setTimeout(() => setDraggingId(id), 0); // let UI update before changing appearance
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
    if (itemId) {
      updateStatus.mutate({ id: itemId, status });
    }
  };

  const currentFolderName = selectedFolderId 
    ? (folders.find((f: any) => f.id === selectedFolderId)?.name || 'Thư mục ẩn')
    : 'Tất cả thư mục';

  return (
    <div className="h-full flex flex-col md:flex-row overflow-hidden bg-slate-50 text-slate-900">
      {/* Sidebar */}
      <div className="w-64 border-r border-slate-200 bg-white flex flex-col shrink-0 h-full">
        <div className="p-4 border-b border-slate-100 flex items-center justify-between">
          <h2 className="text-sm font-semibold text-slate-800">Thư mục</h2>
        </div>
        <div className="flex-1 overflow-y-auto p-3 space-y-1 hide-scrollbar">
          <button
            onClick={() => updateFolderUrl(null)}
            className={`w-full flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium transition-colors ${
              selectedFolderId === null ? 'bg-indigo-50 text-indigo-700' : 'text-slate-600 hover:bg-slate-50 hover:text-slate-900'
            }`}
          >
            <Folder className="w-4 h-4" />
            Tất cả
          </button>
          {folders.map((f: any) => (
            <button
              key={f.id}
              onClick={() => updateFolderUrl(f.id)}
              className={`w-full flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium transition-colors ${
                selectedFolderId === f.id ? 'bg-indigo-50 text-indigo-700' : 'text-slate-600 hover:bg-slate-50 hover:text-slate-900'
              }`}
            >
              <span className="w-2 h-2 rounded-full" style={{ backgroundColor: f.color || '#94a3b8' }}></span>
              <span className="truncate">{f.name}</span>
            </button>
          ))}
        </div>
      </div>

      {/* Main Board */}
      <div className="flex-1 flex flex-col min-w-0 overflow-hidden">
        {/* Header */}
        <div className="px-6 py-4 border-b border-slate-200 bg-white flex justify-between items-end shrink-0 flex-wrap gap-4">
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
          </div>
        </div>

        {/* Tools bar */}
        <div className="px-6 py-3 border-b border-slate-200 bg-white flex gap-2">
          <button
            onClick={() => setIsNoteModalOpen(true)}
            className="inline-flex items-center justify-center gap-2 px-3 py-1.5 text-[13px] font-medium text-slate-700 bg-white border border-slate-300 rounded-lg shadow-sm hover:bg-slate-50 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 transition-all"
          >
            <Plus className="w-4 h-4" /> Ghi chú mới
          </button>
          <button
            onClick={() => setIsEventModalOpen(true)}
            className="inline-flex items-center justify-center gap-2 px-3 py-1.5 text-[13px] font-medium text-slate-700 bg-white border border-slate-300 rounded-lg shadow-sm hover:bg-slate-50 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 transition-all"
          >
            <Plus className="w-4 h-4" /> Sự kiện mới
          </button>
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
                      className={`flex-1 flex flex-col gap-2.5 p-2.5 rounded-xl min-h-[160px] transition-colors border-2 ${
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
                            draggable
                            onDragStart={(e) => handleDragStart(e, item.id)}
                            onDragEnd={handleDragEnd}
                            onClick={() => setSelectedItemId(item.id)}
                            className={`bg-white border rounded-xl p-3 cursor-pointer group hover:shadow-md hover:border-slate-300 transition-all ${
                              draggingId === item.id ? 'opacity-40 shadow-none border-slate-200' : 'opacity-100 shadow-sm border-slate-200'
                            }`}
                          >
                            <div className="flex justify-between items-center mb-2">
                              <span className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md text-[11px] font-medium border border-transparent ${typeTileClass(item.type)}`}>
                                {typeIcon(item.type)}
                                {typeLabel(item.type)}
                              </span>
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
    </div>
  );
};
