import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import {
  X, Mail, Calendar, FileText, StickyNote, Briefcase,
  Trash2, Edit3, ExternalLink, Tag, Loader2,
  AlertCircle, Eye, EyeOff, Star, Check, Send, Plus
} from 'lucide-react';
import { itemsApi, foldersApi } from '../lib/itemsApi';
import { connectionsApi } from '../lib/connectionsApi';
import { type PatchItemRequest } from '../types/items';
import { handleApiError } from '../lib/errorUtils';
import toast from 'react-hot-toast';

interface ItemDetailProps {
  itemId: string;
  onClose?: () => void;
  onDeleted?: () => void;
}

const STATUS_LABEL: Record<string, string> = { Inbox: 'Cần xem', Doing: 'Đang xử lý', Done: 'Done' };
const STATUS_COLOR: Record<string, string> = {
  Inbox: 'bg-slate-100 text-slate-600 border border-slate-200',
  Doing: 'bg-blue-50 text-blue-700 border border-blue-100',
  Done: 'bg-emerald-50 text-emerald-700 border border-emerald-100',
};
const STATUS_DOT: Record<string, string> = { Inbox: 'bg-slate-400', Doing: 'bg-blue-500', Done: 'bg-emerald-500' };

const TYPE_INFO: Record<string, { label: string; icon: React.ReactNode; bg: string }> = {
  Email:  { label: 'Email',    icon: <Mail className="w-5 h-5" />,      bg: 'bg-blue-50 text-blue-600' },
  Event:  { label: 'Sự kiện', icon: <Calendar className="w-5 h-5" />,   bg: 'bg-amber-50 text-amber-600' },
  File:   { label: 'Tệp',     icon: <FileText className="w-5 h-5" />,   bg: 'bg-emerald-50 text-emerald-600' },
  Note:   { label: 'Ghi chú', icon: <StickyNote className="w-5 h-5" />, bg: 'bg-slate-100 text-slate-500' },
  Ticket: { label: 'Ticket',  icon: <Briefcase className="w-5 h-5" />,  bg: 'bg-purple-50 text-purple-600' },
};

export const ItemDetail: React.FC<ItemDetailProps> = ({ itemId, onClose, onDeleted }) => {
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  const [isEditing, setIsEditing] = useState(false);
  const [newLabelName, setNewLabelName] = useState('');
  const [isAddingLabel, setIsAddingLabel] = useState(false);
  const [isAddingToFolder, setIsAddingToFolder] = useState(false);

  // Event form edit state
  const [eventForm, setEventForm] = useState({
    title: '',
    start: '',
    end: '',
    location: '',
    attendees: ''
  });

  // File edit state
  const [fileName, setFileName] = useState('');
  const [isRenamingFile, setIsRenamingFile] = useState(false);

  // Fetch item by ID
  const { data: item, isLoading, isError, refetch } = useQuery({
    queryKey: ['item', itemId],
    queryFn: () => itemsApi.getItemById(itemId),
    enabled: !!itemId,
  });

  const { data: folders = [] } = useQuery({
    queryKey: ['folders'],
    queryFn: () => foldersApi.getFolders()
  });

  // Mutate item (writeback PATCH)
  const patchMutation = useMutation({
    mutationFn: (payload: PatchItemRequest) => itemsApi.patchItem(itemId, payload),
    onSuccess: () => {
      toast.success('Đã lưu thay đổi thành công');
      setIsEditing(false);
      setIsRenamingFile(false);
      setNewLabelName('');
      setIsAddingLabel(false);

      queryClient.invalidateQueries({ queryKey: ['item', itemId] });
      queryClient.invalidateQueries({ queryKey: ['items'] });
    },
    onError: (err) => {
      handleApiError(err, 'Lỗi cập nhật dữ liệu', {
        onConflict: async () => {
          if (item?.connectionId) {
            try {
              await connectionsApi.syncConnection(item.connectionId);
            } catch (e) {
              console.error('Lỗi khi đồng bộ tự động', e);
            }
          }
          refetch();
          queryClient.invalidateQueries({ queryKey: ['items'] });
        },
        navigate
      });
    }
  });

  // Delete item mutation
  const deleteMutation = useMutation({
    mutationFn: () => itemsApi.deleteItem(itemId),
    onSuccess: () => {
      toast.success('Đã xóa mục thành công');
      queryClient.invalidateQueries({ queryKey: ['items'] });
      if (onDeleted) onDeleted();
      if (onClose) onClose();
    },
    onError: (err) => {
      handleApiError(err, 'Không thể xóa dữ liệu', {
        onConflict: async () => {
          if (item?.connectionId) {
            try {
              await connectionsApi.syncConnection(item.connectionId);
            } catch (e) {
              console.error('Lỗi khi đồng bộ tự động', e);
            }
          }
          refetch();
          queryClient.invalidateQueries({ queryKey: ['items'] });
        },
        navigate
      });
    }
  });

  const addToFolderMutation = useMutation({
    mutationFn: (folderId: string) => foldersApi.addItemToFolder(folderId, { itemId }),
    onSuccess: () => {
      toast.success('Đã thêm vào thư mục');
      queryClient.invalidateQueries({ queryKey: ['item', itemId] });
      queryClient.invalidateQueries({ queryKey: ['items'] });
    },
    onError: (err) => handleApiError(err, 'Lỗi thêm vào thư mục', { navigate })
  });

  const removeFromFolderMutation = useMutation({
    mutationFn: (folderId: string) => foldersApi.removeItemFromFolder(folderId, itemId),
    onSuccess: () => {
      toast.success('Đã xóa khỏi thư mục');
      queryClient.invalidateQueries({ queryKey: ['item', itemId] });
      queryClient.invalidateQueries({ queryKey: ['items'] });
    },
    onError: (err) => handleApiError(err, 'Lỗi xóa khỏi thư mục', { navigate })
  });

  if (isLoading) {
    return (
      <div className="fixed inset-0 z-50 flex justify-end">
        <div onClick={onClose} className="absolute inset-0 bg-slate-900/40" />
        <div className="relative w-full max-w-[462px] bg-white border-l border-slate-200 shadow-2xl flex items-center justify-center">
          <div className="flex flex-col items-center space-y-3">
            <Loader2 className="w-8 h-8 animate-spin text-indigo-600" />
            <span className="text-sm font-medium text-slate-500">Đang tải chi tiết...</span>
          </div>
        </div>
      </div>
    );
  }

  if (isError || !item) {
    return (
      <div className="fixed inset-0 z-50 flex justify-end">
        <div onClick={onClose} className="absolute inset-0 bg-slate-900/40" />
        <div className="relative w-full max-w-[462px] bg-white border-l border-slate-200 shadow-2xl flex flex-col items-center justify-center p-6 text-slate-500">
          <AlertCircle className="w-12 h-12 text-rose-500 mb-3" />
          <h3 className="text-base font-semibold text-slate-850 mb-1">Không thể tải thông tin chi tiết</h3>
          <p className="text-xs text-slate-450 text-center max-w-xs mb-4">Vui lòng thử lại sau hoặc tải lại trang.</p>
          <button
            onClick={() => refetch()}
            className="px-4 py-2 bg-indigo-600 text-white rounded-lg text-sm font-medium hover:bg-indigo-700 transition-colors"
          >
            Tải lại
          </button>
        </div>
      </div>
    );
  }

  const metadata = (() => {
    try { return item.metadataJson ? JSON.parse(item.metadataJson) : {}; }
    catch { return {}; }
  })();

  const tInfo = TYPE_INFO[item.type] ?? TYPE_INFO.Note;
  const statusLabel = STATUS_LABEL[item.status] ?? item.status;
  const statusColor = STATUS_COLOR[item.status] ?? 'bg-slate-100 text-slate-500';
  const statusDot = STATUS_DOT[item.status] ?? 'bg-slate-400';

  const typeChip = `inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[11.5px] font-semibold ${tInfo.bg}`;

  // ── metadata rows per type
  const rows: { label: string; value: React.ReactNode }[] = [];
  if (item.type === 'Email') {
    if (metadata.from) rows.push({ label: 'Từ', value: metadata.from });
    const to = Array.isArray(metadata.to) ? metadata.to.join(', ') : metadata.to;
    if (to)            rows.push({ label: 'Đến', value: to });
    if (metadata.labels && metadata.labels.length > 0) {
      rows.push({
        label: 'Nhãn',
        value: (
          <div className="flex flex-wrap gap-x-1.5 gap-y-1 items-center">
            {metadata.labels.map((label: string, idx: number) => (
              <span key={label} className="inline-flex items-center gap-0.5">
                {label}
                <button onClick={() => handleRemoveLabel(label)} className="text-slate-400 hover:text-rose-500 ml-0.5">&times;</button>
                {idx < metadata.labels.length - 1 && <span className="text-slate-900">,</span>}
              </span>
            ))}
          </div>
        )
      });
    }
    rows.push({ label: 'Thời gian', value: new Date(item.occurredAt).toLocaleString('vi-VN') });
  } else if (item.type === 'Event') {
    rows.push({ label: 'Bắt đầu', value: metadata.start ? new Date(metadata.start).toLocaleString('vi-VN') : new Date(item.occurredAt).toLocaleString('vi-VN') });
    if (metadata.end) rows.push({ label: 'Kết thúc', value: new Date(metadata.end).toLocaleString('vi-VN') });
    if (metadata.location) rows.push({ label: 'Địa điểm', value: metadata.location });
    if (Array.isArray(metadata.attendees) && metadata.attendees.length) {
      rows.push({
        label: 'Người tham gia',
        value: (
          <div className="flex flex-wrap gap-1 mt-1">
            {metadata.attendees.map((email: string) => (
              <span key={email} className="px-2 py-0.5 bg-slate-100 text-slate-700 rounded text-[11px]">
                {email}
              </span>
            ))}
          </div>
        )
      });
    }
  } else if (item.type === 'File') {
    rows.push({ label: 'Được tạo', value: new Date(item.occurredAt).toLocaleString('vi-VN') });
    if (metadata.mimeType) rows.push({ label: 'Loại tệp', value: metadata.mimeType });
    if (metadata.size) {
      const kb = Math.round(metadata.size / 1024);
      rows.push({ label: 'Kích thước', value: kb >= 1024 ? `${(kb / 1024).toFixed(1)} MB` : `${kb} KB` });
    }
  } else if (item.type === 'Note') {
    rows.push({ label: 'Được tạo', value: new Date(item.occurredAt).toLocaleString('vi-VN') });
  } else if (item.type === 'Ticket') {
    if (metadata.issueKey)   rows.push({ label: 'Issue Key',  value: metadata.issueKey });
    // if (metadata.projectKey) rows.push({ label: 'Project',    value: metadata.projectKey }); // Removed per user request
    if (metadata.issueType)  rows.push({ label: 'Loại',       value: metadata.issueType });
    if (metadata.priority)   rows.push({ label: 'Ưu tiên',    value: metadata.priority });
    if (metadata.assignee)   rows.push({ label: 'Assignee',   value: metadata.assignee });
    if (metadata.reporter)   rows.push({ label: 'Reporter',   value: metadata.reporter });
    if (metadata.status)     rows.push({ label: 'Trạng thái', value: metadata.status });
    if (Array.isArray(metadata.labels) && metadata.labels.length) {
      rows.push({
        label: 'Labels',
        value: (
          <div className="flex flex-wrap gap-1 mt-1">
            {metadata.labels.map((l: string) => (
              <span key={l} className="px-2 py-0.5 bg-purple-50 text-purple-750 border border-purple-100 rounded text-[11px]">
                {l}
              </span>
            ))}
          </div>
        )
      });
    }
    if (item.dueAt) rows.push({ label: 'Due date', value: new Date(item.dueAt).toLocaleString('vi-VN') });
  }

  // Event form edits
  const startEditingEvent = () => {
    let startVal = '';
    let endVal = '';
    if (metadata.start) startVal = new Date(metadata.start).toISOString().slice(0, 16);
    if (metadata.end)   endVal = new Date(metadata.end).toISOString().slice(0, 16);

    setEventForm({
      title: item.title,
      start: startVal || new Date(item.occurredAt).toISOString().slice(0, 16),
      end: endVal,
      location: metadata.location || '',
      attendees: metadata.attendees ? metadata.attendees.join(', ') : ''
    });
    setIsEditing(true);
  };

  const handleSaveEvent = () => {
    if (!eventForm.title || !eventForm.start || !eventForm.end) {
      toast.error('Vui lòng điền đầy đủ tiêu đề, thời gian bắt đầu và kết thúc');
      return;
    }
    const startIso = new Date(eventForm.start).toISOString();
    const endIso = new Date(eventForm.end).toISOString();

    if (new Date(startIso) >= new Date(endIso)) {
      toast.error('Thời gian bắt đầu phải trước thời gian kết thúc');
      return;
    }

    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    const attendeesArray = eventForm.attendees
      ? eventForm.attendees.split(',').map(email => email.trim()).filter(email => email.length > 0)
      : [];

    const invalidEmails = attendeesArray.filter(email => !emailRegex.test(email));
    if (invalidEmails.length > 0) {
      toast.error(`Email không hợp lệ: ${invalidEmails.join(', ')}`);
      return;
    }

    patchMutation.mutate({
      title: eventForm.title,
      start: startIso,
      end: endIso,
      location: eventForm.location || undefined,
      attendees: attendeesArray
    });
  };

  // File rename edits
  const startRenamingFile = () => {
    setFileName(item.title);
    setIsRenamingFile(true);
  };

  const handleRenameFile = () => {
    if (!fileName.trim()) {
      toast.error('Tên tệp không được để trống');
      return;
    }
    if (fileName === item.title) {
      setIsRenamingFile(false);
      return;
    }
    patchMutation.mutate({
      name: fileName.trim()
    });
  };

  // Gmail labels
  const handleAddLabel = () => {
    if (!newLabelName.trim()) return;
    patchMutation.mutate({
      addLabels: [newLabelName.trim().toUpperCase()]
    });
  };

  const handleRemoveLabel = (labelName: string) => {
    patchMutation.mutate({
      removeLabels: [labelName]
    });
  };

  const bodyText: string =
    metadata.body ?? metadata.description ?? metadata.contentMarkdown ?? item.snippet ?? '';

  return (
    <div className="fixed inset-0 z-50 flex justify-end">
      {/* Backdrop */}
      <div onClick={onClose} className="absolute inset-0 bg-slate-900/40" />

      {/* Drawer */}
      <div className="relative w-full max-w-[462px] bg-white border-l border-slate-200 shadow-2xl flex flex-col" style={{ animation: 'wh-slide-in .25s ease' }}>

        {/* Header */}
        <div className="px-5 py-[18px] border-b border-slate-200 shrink-0">
          <div className="flex items-start justify-between mb-3.5">
            <div className="flex items-center gap-3">
              <div className={`w-[42px] h-[42px] rounded-xl flex items-center justify-center shrink-0 ${tInfo.bg}`}>
                {tInfo.icon}
              </div>
              <div className="flex flex-wrap gap-[6px]">
                <span className={typeChip}>{tInfo.label}</span>
                <span className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[11.5px] font-semibold ${statusColor}`}>
                  <span className={`w-1.5 h-1.5 rounded-full ${statusDot}`} />
                  {statusLabel}
                </span>
              </div>
            </div>
            <button onClick={onClose} className="p-1.5 text-slate-400 hover:text-slate-700 hover:bg-slate-100 rounded-lg shrink-0 transition-colors -mr-1.5 -mt-1.5">
              <X className="w-5 h-5" />
            </button>
          </div>
          {isRenamingFile ? (
            <div className="flex items-center gap-2 mt-1">
              <input
                type="text"
                value={fileName}
                onChange={e => setFileName(e.target.value)}
                className="flex-1 min-w-0 bg-white border border-indigo-400 rounded-lg px-2.5 py-1.5 text-[15px] font-semibold focus:outline-none focus:ring-2 focus:ring-indigo-500/20"
                onKeyDown={e => {
                  if (e.key === 'Enter') handleRenameFile();
                  else if (e.key === 'Escape') setIsRenamingFile(false);
                }}
                autoFocus
              />
              <button
                onClick={handleRenameFile}
                disabled={patchMutation.isPending}
                className="p-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors"
              >
                {patchMutation.isPending ? <Loader2 className="w-4.5 h-4.5 animate-spin" /> : <Check className="w-4.5 h-4.5" />}
              </button>
            </div>
          ) : (
            <h2 className="text-[20px] font-bold text-slate-800 leading-[1.35] m-0">{item.title}</h2>
          )}
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto px-5 py-[18px]">
          {/* Folders */}
          <div className="flex flex-wrap items-center gap-2 mb-4">
            {item.folderIds?.map(fId => {
              const f = folders.find((fol: any) => fol.id === fId);
              if (!f) return null;
              return (
                <div key={f.id} className="inline-flex items-center gap-[6px] text-[12.5px] text-slate-500 bg-slate-100 pl-[11px] pr-1 py-1 rounded-full group">
                  <span className="w-2 h-2 rounded-full" style={{ backgroundColor: f.color || '#f59e0b' }}></span>
                  <span className="mr-0.5">{f.name}</span>
                  <button 
                    onClick={() => removeFromFolderMutation.mutate(f.id)}
                    disabled={removeFromFolderMutation.isPending}
                    className="p-0.5 rounded-full text-slate-400 hover:bg-slate-200 hover:text-rose-500 opacity-0 group-hover:opacity-100 transition-all"
                  >
                    <X className="w-3 h-3" />
                  </button>
                </div>
              );
            })}
            
            {/* Add to folder button & dropdown */}
            <div className="relative">
              <button 
                onClick={() => setIsAddingToFolder(!isAddingToFolder)}
                className="inline-flex items-center justify-center gap-1 h-[26px] px-2 rounded-full bg-slate-50 border border-slate-200 text-slate-500 hover:bg-slate-100 hover:text-slate-700 transition-colors text-[12px] font-medium"
                title="Thêm vào thư mục"
              >
                <Plus className="w-3.5 h-3.5" />
                <span>Thêm</span>
              </button>
              
              {isAddingToFolder && (
                <div className="absolute top-full left-0 mt-1.5 w-48 bg-white border border-slate-200 shadow-xl rounded-lg py-1.5 z-[60] animate-in fade-in zoom-in-95 duration-100">
                  {folders.filter((f: any) => !item.folderIds?.includes(f.id)).length === 0 ? (
                    <div className="px-3 py-2 text-xs text-slate-500 text-center">Không còn thư mục nào</div>
                  ) : (
                    folders.filter((f: any) => !item.folderIds?.includes(f.id)).map((f: any) => (
                      <button
                        key={f.id}
                        onClick={() => {
                          addToFolderMutation.mutate(f.id);
                          setIsAddingToFolder(false);
                        }}
                        disabled={addToFolderMutation.isPending}
                        className="w-full text-left px-3 py-2 text-[13px] font-medium text-slate-700 hover:bg-slate-50 flex items-center gap-2.5 transition-colors"
                      >
                        <span className="w-2 h-2 rounded-full" style={{ backgroundColor: f.color || '#f59e0b' }}></span>
                        <span className="truncate">{f.name}</span>
                      </button>
                    ))
                  )}
                </div>
              )}
            </div>
          </div>

          {/* Form edit for Event */}
          {isEditing && item.type === 'Event' ? (
            <div className="border border-slate-200 rounded-[10px] p-4 bg-slate-50/50 space-y-4 mb-[18px]">
              <h3 className="text-xs font-bold text-slate-500 uppercase tracking-wider">Chỉnh sửa sự kiện</h3>
              <div>
                <label className="block text-xs font-semibold text-slate-500 mb-1">Tiêu đề sự kiện</label>
                <input
                  type="text"
                  value={eventForm.title}
                  onChange={e => setEventForm({...eventForm, title: e.target.value})}
                  className="w-full bg-white border border-slate-200 rounded-lg px-3 py-2 text-slate-900 text-sm focus:outline-none focus:border-indigo-500"
                />
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-xs font-semibold text-slate-500 mb-1">Bắt đầu (Local)</label>
                  <input
                    type="datetime-local"
                    value={eventForm.start}
                    onChange={e => setEventForm({...eventForm, start: e.target.value})}
                    className="w-full bg-white border border-slate-200 rounded-lg px-3 py-2 text-slate-900 text-sm focus:outline-none focus:border-indigo-500"
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-slate-500 mb-1">Kết thúc (Local)</label>
                  <input
                    type="datetime-local"
                    value={eventForm.end}
                    onChange={e => setEventForm({...eventForm, end: e.target.value})}
                    className="w-full bg-white border border-slate-200 rounded-lg px-3 py-2 text-slate-900 text-sm focus:outline-none focus:border-indigo-500"
                  />
                </div>
              </div>
              <div>
                <label className="block text-xs font-semibold text-slate-500 mb-1">Địa điểm</label>
                <input
                  type="text"
                  value={eventForm.location}
                  onChange={e => setEventForm({...eventForm, location: e.target.value})}
                  className="w-full bg-white border border-slate-200 rounded-lg px-3 py-2 text-slate-900 text-sm focus:outline-none focus:border-indigo-500"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-slate-500 mb-1">Người tham gia (Ngăn cách bởi dấu phẩy)</label>
                <input
                  type="text"
                  value={eventForm.attendees}
                  onChange={e => setEventForm({...eventForm, attendees: e.target.value})}
                  placeholder="vd1@gmail.com, vd2@gmail.com"
                  className="w-full bg-white border border-slate-200 rounded-lg px-3 py-2 text-slate-900 text-sm focus:outline-none focus:border-indigo-500"
                />
              </div>
              <div className="flex justify-end gap-2 pt-2">
                <button
                  onClick={() => setIsEditing(false)}
                  className="px-3.5 py-2 border border-slate-200 rounded-lg text-xs font-medium text-slate-650 hover:bg-slate-100 transition-colors"
                >
                  Hủy
                </button>
                <button
                  onClick={handleSaveEvent}
                  disabled={patchMutation.isPending}
                  className="px-3.5 py-2 bg-indigo-600 text-white rounded-lg text-xs font-semibold hover:bg-indigo-750 transition-colors flex items-center gap-1.5"
                >
                  {patchMutation.isPending && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
                  <span>Lưu</span>
                </button>
              </div>
            </div>
          ) : (
            /* Metadata Rows */
            rows.length > 0 && (
              <div className="border border-slate-200 rounded-[10px] overflow-hidden mb-[18px]">
                {rows.map((row, i) => (
                  <div key={i} className="flex gap-3 px-[13px] py-[9px] border-b border-slate-200 last:border-b-0">
                    <span className="text-[12.5px] text-slate-400 w-[118px] shrink-0">{row.label}</span>
                    <span className="text-[12.5px] text-slate-900 flex-1 break-words">{row.value}</span>
                  </div>
                ))}
              </div>
            )
          )}

          {item.type === 'Email' && isAddingLabel && (
            <div className="mb-4 flex items-center gap-2 bg-slate-50 border border-slate-200 rounded-lg px-3 py-2">
              <span className="text-[12px] font-semibold text-slate-500 shrink-0">Thêm nhãn:</span>
              <input
                type="text"
                value={newLabelName}
                onChange={e => setNewLabelName(e.target.value)}
                placeholder="NHÃN MỚI"
                className="bg-white border border-slate-200 rounded px-2 py-1 text-[13px] focus:outline-none focus:border-indigo-500 flex-1 min-w-0"
                onKeyDown={e => {
                  if (e.key === 'Enter') { handleAddLabel(); setIsAddingLabel(false); }
                  else if (e.key === 'Escape') setIsAddingLabel(false);
                }}
                autoFocus
              />
              <button onClick={() => { handleAddLabel(); setIsAddingLabel(false); }} className="text-[12px] text-indigo-650 hover:text-indigo-800 font-semibold px-2.5 py-1.5 bg-indigo-50 hover:bg-indigo-100 rounded transition-colors shrink-0">Thêm</button>
              <button onClick={() => setIsAddingLabel(false)} className="text-[12px] text-slate-500 hover:text-slate-800 font-semibold px-2.5 py-1.5 hover:bg-slate-100 rounded transition-colors shrink-0">Hủy</button>
            </div>
          )}

          {/* Body Content */}
          <div className="text-[11px] font-semibold tracking-[0.04em] uppercase text-slate-400 mb-2">Nội dung</div>
          <div className="text-[13.5px] text-slate-900 leading-[1.65] whitespace-pre-wrap bg-slate-50 rounded-[10px] p-[14px]">
            {bodyText || <span className="text-slate-400 italic">Không có nội dung</span>}
          </div>
        </div>

        {/* Footer actions — per type */}
        <div className="shrink-0 border-t border-slate-200 px-5 py-[14px] flex flex-wrap gap-2">
          {item.type === 'Email' && (
            <>
              <button
                onClick={() => patchMutation.mutate({ isUnread: !metadata.isUnread })}
                disabled={patchMutation.isPending}
                className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-white border border-slate-200 text-slate-700 hover:bg-slate-50 shadow-sm transition-colors"
              >
                {metadata.isUnread ? <Eye className="w-4 h-4 text-slate-500" /> : <EyeOff className="w-4 h-4 text-slate-500" />}
                <span>{metadata.isUnread ? 'Đánh dấu đã đọc' : 'Đánh dấu chưa đọc'}</span>
              </button>
              <button
                onClick={() => patchMutation.mutate({ isStarred: !metadata.isStarred })}
                disabled={patchMutation.isPending}
                className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-white border border-slate-200 text-slate-700 hover:bg-slate-50 shadow-sm transition-colors"
              >
                <Star className={`w-4 h-4 ${metadata.isStarred ? 'fill-amber-400 text-amber-400' : 'text-slate-450'}`} />
                <span>{metadata.isStarred ? 'Bỏ quan trọng' : 'Quan trọng'}</span>
              </button>
              <button
                onClick={() => setIsAddingLabel(true)}
                className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-medium text-slate-600 hover:bg-slate-100 transition-colors"
              >
                <Tag className="w-4 h-4 text-slate-400" /><span>Nhãn</span>
              </button>
              <button
                onClick={() => navigate('/scheduled')}
                className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-medium text-slate-600 hover:bg-slate-100 transition-colors"
              >
                <Send className="w-4 h-4 text-slate-400" /><span>Soạn mới</span>
              </button>
              <button
                onClick={() => {
                  if (window.confirm('Bạn có muốn xóa vĩnh viễn email này trên Gmail?')) {
                    deleteMutation.mutate();
                  }
                }}
                disabled={deleteMutation.isPending}
                className="w-[36px] h-[36px] inline-flex items-center justify-center rounded-lg bg-rose-50 border border-rose-200 text-rose-600 hover:bg-rose-100 transition-colors"
              >
                {deleteMutation.isPending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Trash2 className="w-4 h-4" />}
              </button>
            </>
          )}

          {item.type === 'Event' && (
            <>
              {!isEditing && (
                <button
                  onClick={startEditingEvent}
                  className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-indigo-650 text-white hover:bg-indigo-755 shadow-sm transition-colors"
                >
                  <Edit3 className="w-4 h-4" /><span>Sửa sự kiện</span>
                </button>
              )}
              {metadata.meetUrl && (
                <a
                  href={metadata.meetUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-white border border-slate-200 text-slate-700 hover:bg-slate-50 shadow-sm transition-colors"
                >
                  <ExternalLink className="w-4 h-4 text-slate-500" /><span>Google Meet</span>
                </a>
              )}
              <button
                onClick={() => {
                  if (window.confirm('Bạn có chắc muốn xóa sự kiện này?')) {
                    deleteMutation.mutate();
                  }
                }}
                disabled={deleteMutation.isPending}
                className="w-[36px] h-[36px] inline-flex items-center justify-center rounded-lg bg-rose-50 border border-rose-200 text-rose-600 hover:bg-rose-100 transition-colors"
              >
                {deleteMutation.isPending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Trash2 className="w-4 h-4" />}
              </button>
            </>
          )}

          {item.type === 'File' && (
            <>
              <button
                onClick={startRenamingFile}
                className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-indigo-600 text-white hover:bg-indigo-700 shadow-sm transition-colors"
              >
                <Edit3 className="w-4 h-4" /><span>Đổi tên</span>
              </button>
              {metadata.webViewLink && (
                <a
                  href={metadata.webViewLink}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-white border border-slate-200 text-slate-700 hover:bg-slate-50 shadow-sm transition-colors"
                >
                  <ExternalLink className="w-4 h-4 text-slate-500" /><span>Mở trên Drive</span>
                </a>
              )}
              <button
                onClick={() => {
                  if (window.confirm('Bạn có muốn xóa tệp này trên Google Drive?')) {
                    deleteMutation.mutate();
                  }
                }}
                disabled={deleteMutation.isPending}
                className="w-[36px] h-[36px] inline-flex items-center justify-center rounded-lg bg-rose-50 border border-rose-200 text-rose-600 hover:bg-rose-100 transition-colors"
              >
                {deleteMutation.isPending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Trash2 className="w-4 h-4" />}
              </button>
            </>
          )}
        </div>

      </div>

      {/* slide-in animation */}
      <style>{`
        @keyframes wh-slide-in { from { transform: translateX(100%); } to { transform: translateX(0); } }
      `}</style>
    </div>
  );
};
