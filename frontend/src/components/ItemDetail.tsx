import React, { useState, useRef, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import {
  X, Mail, Calendar, FileText, StickyNote, Briefcase,
  Trash2, Edit3, ExternalLink, Loader2, Tag,
  AlertCircle, Eye, EyeOff, Star, Check, Send, Plus,
  Share2, FolderPlus, Folder,
} from 'lucide-react';
import { itemsApi, foldersApi } from '../lib/itemsApi';
import { tagsApi } from '../lib/tagsApi';
import { TagChip, FolderChip } from './tags/TagChip';
import { TagManagerModal } from './tags/TagManagerModal';
import { connectionsApi } from '../lib/connectionsApi';
import { type PatchItemRequest, type FolderResponse, type ItemResponse, type PagedResult } from '../types/items';
import { handleApiError } from '../lib/errorUtils';
import { getStatusLabel, isItemUnread, isDriveFolder } from '../lib/itemMeta';
import { useSeenSet, markSeen, markUnseen } from '../lib/seenStore';
import { typeLabelKey } from '../lib/itemVisuals';
import { useI18n } from '../hooks/useI18n';
import toast from 'react-hot-toast';
import { DriveShareDialog } from './drive/DriveShareDialog';
import { CreateDriveFolderModal } from './drive/CreateDriveFolderModal';

interface ItemDetailProps {
  itemId: string;
  onClose?: () => void;
  onDeleted?: () => void;
}

/** Cập nhật cờ đọc/chưa đọc trong metadataJson (cho optimistic update — đỡ lag khi mark read). */
function withUnreadFlag(item: ItemResponse, unread: boolean): ItemResponse {
  let meta: Record<string, unknown>;
  try { meta = item.metadataJson ? JSON.parse(item.metadataJson) : {}; } catch { meta = {}; }
  meta.isUnread = unread;
  if (Array.isArray(meta.labels)) {
    const labels = (meta.labels as string[]).filter((l) => l !== 'UNREAD');
    if (unread) labels.push('UNREAD');
    meta.labels = labels;
  }
  return { ...item, metadataJson: JSON.stringify(meta) };
}

const STATUS_COLOR: Record<string, string> = {
  Inbox: 'bg-slate-100 text-slate-600 border border-slate-200 dark:bg-slate-800 dark:text-slate-400 dark:border-slate-700',
  Doing: 'bg-blue-50 text-blue-700 border border-blue-100 dark:bg-blue-500/10 dark:text-blue-400 dark:border-blue-500/20',
  Done: 'bg-emerald-50 text-emerald-700 border border-emerald-100 dark:bg-emerald-500/10 dark:text-emerald-400 dark:border-emerald-500/20',
};
const STATUS_DOT: Record<string, string> = { Inbox: 'bg-slate-400', Doing: 'bg-blue-500', Done: 'bg-emerald-500' };

const TYPE_INFO: Record<string, { label: string; icon: React.ReactNode; bg: string }> = {
  Email: { label: 'Email', icon: <Mail className="w-5 h-5" />, bg: 'bg-blue-50 text-blue-600 dark:bg-blue-500/10 dark:text-blue-400' },
  Event: { label: 'Sự kiện', icon: <Calendar className="w-5 h-5" />, bg: 'bg-amber-50 text-amber-600 dark:bg-amber-500/10 dark:text-amber-400' },
  File: { label: 'Tệp', icon: <FileText className="w-5 h-5" />, bg: 'bg-emerald-50 text-emerald-600 dark:bg-emerald-500/10 dark:text-emerald-400' },
  Note: { label: 'Ghi chú', icon: <StickyNote className="w-5 h-5" />, bg: 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400' },
  Ticket: { label: 'Ticket', icon: <Briefcase className="w-5 h-5" />, bg: 'bg-purple-50 text-purple-600 dark:bg-purple-500/10 dark:text-purple-400' },
};

export const ItemDetail: React.FC<ItemDetailProps> = ({ itemId, onClose, onDeleted }) => {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { t, lang } = useI18n();
  const dl = lang === 'vi' ? 'vi-VN' : 'en-US';
  const seenSet = useSeenSet();

  const [isEditing, setIsEditing] = useState(false);
  const [isAddingToFolder, setIsAddingToFolder] = useState(false);
  const addFolderRef = useRef<HTMLDivElement>(null);
  const [isAddingTag, setIsAddingTag] = useState(false);
  const addTagRef = useRef<HTMLDivElement>(null);
  const [tagManagerOpen, setTagManagerOpen] = useState(false);

  // Đóng dropdown "Thêm vào thư mục" khi click ra ngoài / nhấn Esc.
  useEffect(() => {
    if (!isAddingToFolder) return;
    const onDocClick = (e: MouseEvent) => {
      if (addFolderRef.current && !addFolderRef.current.contains(e.target as Node)) setIsAddingToFolder(false);
    };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setIsAddingToFolder(false); };
    document.addEventListener('mousedown', onDocClick);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDocClick);
      document.removeEventListener('keydown', onKey);
    };
  }, [isAddingToFolder]);

  // Đóng dropdown "Thêm tag" khi click ra ngoài / nhấn Esc.
  useEffect(() => {
    if (!isAddingTag) return;
    const onDocClick = (e: MouseEvent) => {
      if (addTagRef.current && !addTagRef.current.contains(e.target as Node)) setIsAddingTag(false);
    };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setIsAddingTag(false); };
    document.addEventListener('mousedown', onDocClick);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDocClick);
      document.removeEventListener('keydown', onKey);
    };
  }, [isAddingTag]);

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
  const [driveShareOpen, setDriveShareOpen] = useState(false);
  const [createSubfolderOpen, setCreateSubfolderOpen] = useState(false);

  // Fetch item by ID.
  // placeholderData: mồi từ cache list/board đang có → drawer mở TỨC THÌ với data sẵn,
  // fetch chi tiết chạy nền — không còn màn spinner nháy trước khi hiện nội dung.
  const { data: item, isLoading, isError, refetch } = useQuery({
    queryKey: ['item', itemId],
    queryFn: () => itemsApi.getItemById(itemId),
    enabled: !!itemId,
    placeholderData: () => {
      for (const [, data] of queryClient.getQueriesData<unknown>({ queryKey: ['items'] })) {
        if (!data) continue;
        const asInfinite = data as { pages?: { items?: ItemResponse[] }[] };
        const asPaged = data as { items?: ItemResponse[] };
        const arr: ItemResponse[] = Array.isArray(asInfinite.pages)
          ? asInfinite.pages.flatMap(pg => pg.items ?? [])
          : (asPaged.items ?? []);
        const found = arr.find(i => i.id === itemId);
        if (found) return found;
      }
      return undefined;
    },
  });

  const { data: folders = [] } = useQuery({
    queryKey: ['folders'],
    queryFn: () => foldersApi.getFolders()
  });

  // Mutate item (writeback PATCH)
  const patchMutation = useMutation({
    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    mutationFn: ({ _isAutoRead, ...payload }: PatchItemRequest & { _isAutoRead?: boolean }) => itemsApi.patchItem(itemId, payload),
    // Optimistic cho read/unread: cập nhật cache NGAY để UI (row + drawer) đổi tức thì,
    // không chờ round-trip Gmail (nguồn gây "mark as read khá lag"). Rollback nếu lỗi.
    onMutate: async (variables) => {
      if (variables.isUnread === undefined) return;
      const unread = variables.isUnread;
      await queryClient.cancelQueries({ queryKey: ['item', itemId] });
      const prevItem = queryClient.getQueryData<ItemResponse>(['item', itemId]);
      const prevLists = queryClient.getQueriesData<PagedResult<ItemResponse>>({ queryKey: ['items'] });
      queryClient.setQueryData<ItemResponse>(['item', itemId], (old) => old ? withUnreadFlag(old, unread) : old);
      queryClient.setQueriesData<PagedResult<ItemResponse>>({ queryKey: ['items'] }, (old) =>
        old?.items ? { ...old, items: old.items.map((it) => it.id === itemId ? withUnreadFlag(it, unread) : it) } : old
      );
      return { prevItem, prevLists };
    },
    onSuccess: (_, variables) => {
      if (!variables._isAutoRead) {
        toast.success(t('item.saved'));
      }
      setIsEditing(false);
      setIsRenamingFile(false);

      queryClient.invalidateQueries({ queryKey: ['item', itemId] });
      queryClient.invalidateQueries({ queryKey: ['items'] });
    },
    onError: (err, variables, context) => {
      // rollback optimistic read/unread
      if (context?.prevItem !== undefined) queryClient.setQueryData(['item', itemId], context.prevItem);
      context?.prevLists?.forEach(([key, data]) => queryClient.setQueryData(key, data));
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
        navigate,
        silent: variables?._isAutoRead === true
      });
    }
  });

  // Delete item mutation
  const deleteMutation = useMutation({
    mutationFn: () => itemsApi.deleteItem(itemId),
    onSuccess: () => {
      toast.success(t('item.deleted'));
      queryClient.invalidateQueries({ queryKey: ['items'] });
      if (onDeleted) onDeleted();
      if (onClose) onClose();
    },
    onError: (err) => {
      handleApiError(err, t('item.deleteFail'), {
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

  // Đánh dấu quan trọng (isImportant — field nội bộ, áp cho mọi loại item, KHÔNG ghi lên provider).
  const importantMutation = useMutation({
    mutationFn: (isImportant: boolean) => itemsApi.updateItemImportant(itemId, isImportant),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['item', itemId] });
      queryClient.invalidateQueries({ queryKey: ['items'] });
    },
    onError: (err) => handleApiError(err, t('item.saveFail'), { navigate }),
  });

  const addToFolderMutation = useMutation({
    mutationFn: (folderId: string) => foldersApi.addItemToFolder(folderId, { itemId }),
    onSuccess: () => {
      toast.success(t('item.addedToFolder'));
      queryClient.invalidateQueries({ queryKey: ['item', itemId] });
      queryClient.invalidateQueries({ queryKey: ['items'] });
    },
    onError: (err) => handleApiError(err, t('item.addFolderFail'), { navigate })
  });

  const removeFromFolderMutation = useMutation({
    mutationFn: (folderId: string) => foldersApi.removeItemFromFolder(folderId, itemId),
    onSuccess: () => {
      toast.success(t('item.removedFromFolder'));
      queryClient.invalidateQueries({ queryKey: ['item', itemId] });
      queryClient.invalidateQueries({ queryKey: ['items'] });
    },
    onError: (err) => handleApiError(err, t('item.removeFolderFail'), { navigate })
  });

  // ── Tags (SCRUM-71): danh sách tag của user + gắn/gỡ tag khỏi item ──
  const { data: allTags = [] } = useQuery({ queryKey: ['tags'], queryFn: tagsApi.getTags });

  const invalidateAfterTag = () => {
    queryClient.invalidateQueries({ queryKey: ['item', itemId] });
    queryClient.invalidateQueries({ queryKey: ['items'] });
    queryClient.invalidateQueries({ queryKey: ['tags'] });
  };
  const assignTagMutation = useMutation({
    mutationFn: (tagId: string) => tagsApi.assignTag(tagId, itemId),
    onSuccess: invalidateAfterTag,
    onError: (err) => handleApiError(err, t('tag.assignFail'), { navigate })
  });
  const unassignTagMutation = useMutation({
    mutationFn: (tagId: string) => tagsApi.unassignTag(tagId, itemId),
    onSuccess: invalidateAfterTag,
    onError: (err) => handleApiError(err, t('tag.unassignFail'), { navigate })
  });

  const metadata = (() => {
    try { return item?.metadataJson ? JSON.parse(item.metadataJson) : {}; }
    catch { return {}; }
  })();

  const isUnread = item?.type === 'Email' && (
    metadata.isUnread !== undefined
      ? metadata.isUnread === true
      : (Array.isArray(metadata.labels) && metadata.labels.includes('UNREAD'))
  );

  const autoReadProcessedRef = React.useRef(false);

  React.useEffect(() => {
    autoReadProcessedRef.current = false;
  }, [itemId]);

  React.useEffect(() => {
    if (item && item.type === 'Email' && !autoReadProcessedRef.current) {
      autoReadProcessedRef.current = true;
      if (isUnread) {
        patchMutation.mutate({ isUnread: false, _isAutoRead: true });
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [item, isUnread]);

  // Event/File/Note/Ticket: provider không có nhãn read → mở detail = đánh dấu "đã xem" (client-side).
  React.useEffect(() => {
    if (item && item.type !== 'Email') markSeen(item.id);
  }, [item]);

  if (isLoading) {
    return (
      <div className="fixed inset-0 z-50 flex justify-end">
        <div onClick={onClose} className="absolute inset-0 bg-slate-900/40 dark:bg-black/50" />
        <div className="relative w-full max-w-[462px] bg-white dark:bg-slate-900 border-l border-slate-200 dark:border-slate-800 shadow-2xl flex items-center justify-center" style={{ animation: 'wh-slide-in .25s ease' }}>
          <div className="flex flex-col items-center space-y-3">
            <Loader2 className="w-8 h-8 animate-spin text-brand-600 dark:text-brand-400" />
            <span className="text-sm font-medium text-slate-500 dark:text-slate-400">{t('item.loadingDetail')}</span>
          </div>
        </div>
      </div>
    );
  }

  if (isError || !item) {
    return (
      <div className="fixed inset-0 z-50 flex justify-end">
        <div onClick={onClose} className="absolute inset-0 bg-slate-900/40 dark:bg-black/50" />
        <div className="relative w-full max-w-[462px] bg-white dark:bg-slate-900 border-l border-slate-200 dark:border-slate-800 shadow-2xl flex flex-col items-center justify-center p-6 text-slate-500 dark:text-slate-400" style={{ animation: 'wh-slide-in .25s ease' }}>
          <AlertCircle className="w-12 h-12 text-rose-500 dark:text-rose-400 mb-3" />
          <h3 className="text-base font-semibold text-slate-800 dark:text-slate-100 mb-1">{t('item.loadError')}</h3>
          <p className="text-xs text-slate-400 dark:text-slate-500 text-center max-w-xs mb-4">{t('item.loadErrorHint')}</p>
          <button
            onClick={() => refetch()}
            className="px-4 py-2 bg-brand-600 text-white rounded-lg text-sm font-medium hover:bg-brand-700 transition-colors"
          >
            {t('item.reload')}
          </button>
        </div>
      </div>
    );
  }

  const tInfo = TYPE_INFO[item.type] ?? TYPE_INFO.Note;
  const fileIsDriveFolder = item.type === 'File' && isDriveFolder(item);
  const canDriveShare = item.type === 'File' && !!item.connectionId;
  // "Chưa xem": Email theo Gmail; Event/File/Note theo seenStore (chưa mở trong app). Ticket = false.
  const unread = isItemUnread(item, seenSet);
  // Nhãn: Ticket = status thô từ Jira; còn lại Inbox = Chưa xem/Đã xem theo unread.
  const statusLabel = getStatusLabel(item, t, unread);
  // Màu chip: cam khi Inbox + chưa xem. Ticket LOẠI TRỪ (chip hiển thị status Jira → giữ màu category).
  const showUnread = item.type !== 'Ticket' && item.status === 'Inbox' && unread;
  const statusColor = showUnread
    ? 'bg-amber-50 text-amber-700 border border-amber-200 dark:bg-amber-500/10 dark:text-amber-400 dark:border-amber-500/20'
    : (STATUS_COLOR[item.status] ?? 'bg-slate-100 text-slate-600 border border-slate-200 dark:bg-slate-800 dark:text-slate-400 dark:border-slate-700');
  const statusDot = showUnread
    ? 'bg-amber-500'
    : (STATUS_DOT[item.status] ?? 'bg-slate-400');

  const typeChip = `inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[11.5px] font-semibold ${tInfo.bg}`;

  // ── metadata rows per type
  const rows: { label: string; value: React.ReactNode }[] = [];
  if (item.type === 'Email') {
    if (metadata.from) rows.push({ label: t('item.from'), value: metadata.from });
    const to = Array.isArray(metadata.to) ? metadata.to.join(', ') : metadata.to;
    if (to) rows.push({ label: t('schedEmail.detailTo'), value: to });
    if (metadata.labels && metadata.labels.length > 0) {
      rows.push({
        label: t('item.labels'),
        value: (
          <div className="flex flex-wrap gap-1 items-center">
            {metadata.labels.map((label: string) => (
              <span key={label} className="px-2 py-0.5 bg-slate-100 dark:bg-slate-700/60 text-slate-600 dark:text-slate-300 border border-slate-200 dark:border-slate-600 rounded text-[11px]">
                {label}
              </span>
            ))}
          </div>
        )
      });
    }
    rows.push({ label: t('item.time'), value: new Date(item.occurredAt).toLocaleString(dl) });
  } else if (item.type === 'Event') {
    rows.push({ label: t('item.start'), value: metadata.start ? new Date(metadata.start).toLocaleString(dl) : new Date(item.occurredAt).toLocaleString(dl) });
    if (metadata.end) rows.push({ label: t('item.end'), value: new Date(metadata.end).toLocaleString(dl) });
    if (metadata.location) rows.push({ label: t('item.location'), value: metadata.location });
    if (Array.isArray(metadata.attendees) && metadata.attendees.length) {
      rows.push({
        label: t('item.attendees'),
        value: (
          <div className="flex flex-wrap gap-1 mt-1">
            {metadata.attendees.map((email: string) => (
              <span key={email} className="px-2 py-0.5 bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-200 rounded text-[11px]">
                {email}
              </span>
            ))}
          </div>
        )
      });
    }
  } else if (item.type === 'File') {
    rows.push({ label: t('item.created'), value: new Date(item.occurredAt).toLocaleString(dl) });
    if (metadata.mimeType) rows.push({ label: t('item.fileType'), value: metadata.mimeType });
    if (metadata.size) {
      const kb = Math.round(metadata.size / 1024);
      rows.push({ label: t('item.size'), value: kb >= 1024 ? `${(kb / 1024).toFixed(1)} MB` : `${kb} KB` });
    }
  } else if (item.type === 'Note') {
    rows.push({ label: t('item.created'), value: new Date(item.occurredAt).toLocaleString(dl) });
  } else if (item.type === 'Ticket') {
    if (metadata.issueKey) rows.push({ label: 'Issue Key', value: metadata.issueKey });
    if (metadata.issueType) rows.push({ label: t('item.issueType'), value: metadata.issueType });
    if (metadata.priority) rows.push({ label: t('item.priority'), value: metadata.priority });
    if (metadata.assignee) rows.push({ label: 'Assignee', value: metadata.assignee });
    if (metadata.reporter) rows.push({ label: 'Reporter', value: metadata.reporter });
    if (metadata.status) rows.push({ label: t('item.status'), value: metadata.status });
    if (Array.isArray(metadata.labels) && metadata.labels.length) {
      rows.push({
        label: 'Labels',
        value: (
          <div className="flex flex-wrap gap-1 mt-1">
            {metadata.labels.map((l: string) => (
              <span key={l} className="px-2 py-0.5 bg-purple-50 dark:bg-purple-500/10 text-purple-700 dark:text-purple-400 border border-purple-100 dark:border-purple-500/20 rounded text-[11px]">
                {l}
              </span>
            ))}
          </div>
        )
      });
    }
    if (item.dueAt) rows.push({ label: 'Due date', value: new Date(item.dueAt).toLocaleString(dl) });
  }

  // Event form edits
  const startEditingEvent = () => {
    let startVal = '';
    let endVal = '';
    if (metadata.start) startVal = new Date(metadata.start).toISOString().slice(0, 16);
    if (metadata.end) endVal = new Date(metadata.end).toISOString().slice(0, 16);

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
      toast.error(t('item.eventNeedFields'));
      return;
    }
    const startIso = new Date(eventForm.start).toISOString();
    const endIso = new Date(eventForm.end).toISOString();

    if (new Date(startIso) >= new Date(endIso)) {
      toast.error(t('item.eventTimeOrder'));
      return;
    }

    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    const attendeesArray = eventForm.attendees
      ? eventForm.attendees.split(',').map(email => email.trim()).filter(email => email.length > 0)
      : [];

    const invalidEmails = attendeesArray.filter(email => !emailRegex.test(email));
    if (invalidEmails.length > 0) {
      toast.error(t('item.invalidEmails', { emails: invalidEmails.join(', ') }));
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
      toast.error(t('item.fileNameEmpty'));
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
  const bodyText: string =
    metadata.body ?? metadata.description ?? metadata.contentMarkdown ?? item.snippet ?? '';

  return (
    <div className="fixed inset-0 z-50 flex justify-end">
      {/* Backdrop */}
      <div onClick={onClose} className="absolute inset-0 bg-slate-900/40 dark:bg-black/50" />

      {/* Drawer */}
      <div className="relative w-full max-w-[462px] bg-white dark:bg-slate-900 border-l border-slate-200 dark:border-slate-800 shadow-2xl flex flex-col" style={{ animation: 'wh-slide-in .25s ease' }}>

        {/* Header */}
        <div className="px-5 py-[18px] border-b border-slate-200 dark:border-slate-800 shrink-0">
          <div className="flex items-start justify-between mb-3.5">
            <div className="flex items-center gap-3">
              <div className={`w-[42px] h-[42px] rounded-xl flex items-center justify-center shrink-0 ${tInfo.bg}`}>
                {fileIsDriveFolder ? <Folder className="w-5 h-5" /> : tInfo.icon}
              </div>
              <div className="flex flex-wrap gap-[6px]">
                <span className={typeChip}>{t(typeLabelKey(item.type))}</span>
                <span className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[11.5px] font-semibold ${statusColor}`}>
                  <span className={`w-1.5 h-1.5 rounded-full ${statusDot}`} />
                  {statusLabel}
                </span>
              </div>
            </div>
            <button onClick={onClose} className="p-1.5 text-slate-400 dark:text-slate-500 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 rounded-lg shrink-0 transition-colors -mr-1.5 -mt-1.5">
              <X className="w-5 h-5" />
            </button>
          </div>
          {isRenamingFile ? (
            <div className="flex items-center gap-2 mt-1">
              <input
                type="text"
                value={fileName}
                onChange={e => setFileName(e.target.value)}
                className="flex-1 min-w-0 bg-white dark:bg-slate-800 border border-indigo-400 dark:border-slate-700 rounded-lg px-2.5 py-1.5 text-[15px] font-semibold text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-indigo-500/20"
                onKeyDown={e => {
                  if (e.key === 'Enter') handleRenameFile();
                  else if (e.key === 'Escape') setIsRenamingFile(false);
                }}
                autoFocus
              />
              <button
                onClick={handleRenameFile}
                disabled={patchMutation.isPending}
                className="p-2 bg-brand-600 text-white rounded-lg hover:bg-brand-700 transition-colors"
              >
                {patchMutation.isPending ? <Loader2 className="w-4.5 h-4.5 animate-spin" /> : <Check className="w-4.5 h-4.5" />}
              </button>
            </div>
          ) : (
            <h2 className="text-[20px] font-bold text-slate-800 dark:text-slate-100 leading-[1.35] m-0">{item.title}</h2>
          )}
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto px-5 py-[18px]">
          {/* Folders */}
          <div className="flex flex-wrap items-center gap-2 mb-4">
            {item.folderIds?.map(fId => {
              const f = folders.find((fol: FolderResponse) => fol.id === fId);
              if (!f) return null;
              return (
                <FolderChip
                  key={f.id}
                  name={f.name}
                  color={f.color || '#94a3b8'}
                  onRemove={() => removeFromFolderMutation.mutate(f.id)}
                />
              );
            })}

            {/* Add to folder button & dropdown */}
            <div className="relative" ref={addFolderRef}>
              <button
                onClick={() => setIsAddingToFolder(!isAddingToFolder)}
                className="inline-flex items-center justify-center gap-1 h-[26px] px-2 rounded-full bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-700 hover:text-slate-700 dark:hover:text-slate-200 transition-colors text-[12px] font-medium"
                title={t('item.addToFolder')}
              >
                <Plus className="w-3.5 h-3.5" />
                <span>{t('item.addToFolder')}</span>
              </button>

              {isAddingToFolder && (
                <div className="absolute top-full left-0 mt-1.5 w-48 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 shadow-xl rounded-lg py-1.5 z-[60] animate-in fade-in zoom-in-95 duration-100">
                  {folders.filter((f: FolderResponse) => !item.folderIds?.includes(f.id)).length === 0 ? (
                    <div className="px-3 py-2 text-xs text-slate-500 dark:text-slate-400 text-center">{t('item.noMoreFolders')}</div>
                  ) : (
                    folders.filter((f: FolderResponse) => !item.folderIds?.includes(f.id)).map((f: FolderResponse) => (
                      <button
                        key={f.id}
                        onClick={() => {
                          addToFolderMutation.mutate(f.id);
                          setIsAddingToFolder(false);
                        }}
                        disabled={addToFolderMutation.isPending}
                        className="w-full text-left px-3 py-2 text-[13px] font-medium text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700 flex items-center gap-2.5 transition-colors"
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

          {/* Tags — gắn/gỡ label private của user (SCRUM-71) */}
          <div className="flex flex-wrap items-center gap-2 mb-4">
            {item.tags?.map((tg) => (
              <TagChip key={tg.id} name={tg.name} color={tg.color} onRemove={() => unassignTagMutation.mutate(tg.id)} />
            ))}

            <div className="relative" ref={addTagRef}>
              <button
                onClick={() => setIsAddingTag(!isAddingTag)}
                className="inline-flex items-center justify-center gap-1 h-[26px] px-2 rounded-full bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-700 hover:text-slate-700 dark:hover:text-slate-200 transition-colors text-[12px] font-medium"
                title={t('tag.addTag')}
              >
                <Tag className="w-3.5 h-3.5" />
                <span>{t('tag.addTag')}</span>
              </button>

              {isAddingTag && (
                <div className="absolute top-full left-0 mt-1.5 w-52 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 shadow-xl rounded-lg py-1.5 z-[60] animate-in fade-in zoom-in-95 duration-100">
                  {allTags.filter((tg) => !item.tags?.some((it) => it.id === tg.id)).length === 0 ? (
                    <div className="px-3 py-2 text-xs text-slate-500 dark:text-slate-400 text-center">{t('tag.noneAvailable')}</div>
                  ) : (
                    allTags.filter((tg) => !item.tags?.some((it) => it.id === tg.id)).map((tag) => (
                      <button
                        key={tag.id}
                        onClick={() => { assignTagMutation.mutate(tag.id); setIsAddingTag(false); }}
                        disabled={assignTagMutation.isPending}
                        className="w-full text-left px-3 py-2 text-[13px] font-medium text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700 flex items-center gap-2.5 transition-colors"
                      >
                        <span className="w-2 h-2 rounded-full shrink-0" style={{ backgroundColor: tag.color }} />
                        <span className="truncate">{tag.name}</span>
                      </button>
                    ))
                  )}
                  <div className="mt-1 pt-1 border-t border-slate-100 dark:border-slate-700">
                    <button
                      onClick={() => { setTagManagerOpen(true); setIsAddingTag(false); }}
                      className="w-full text-left px-3 py-2 text-[12.5px] font-medium text-brand-600 dark:text-brand-400 hover:bg-slate-50 dark:hover:bg-slate-700 flex items-center gap-2 transition-colors"
                    >
                      <Edit3 className="w-3.5 h-3.5" />
                      <span>{t('tag.manage')}</span>
                    </button>
                  </div>
                </div>
              )}
            </div>
          </div>

          {/* Form edit for Event */}
          {isEditing && item.type === 'Event' ? (
            <div className="border border-slate-200 dark:border-slate-800 rounded-[10px] p-4 bg-slate-50/50 dark:bg-slate-800/50 space-y-4 mb-[18px]">
              <h3 className="text-xs font-bold text-slate-500 dark:text-slate-400 uppercase tracking-wider">{t('item.editEvent')}</h3>
              <div>
                <label className="block text-xs font-semibold text-slate-500 dark:text-slate-400 mb-1">{t('item.eventTitle')}</label>
                <input
                  type="text"
                  value={eventForm.title}
                  onChange={e => setEventForm({ ...eventForm, title: e.target.value })}
                  className="w-full bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-lg px-3 py-2 text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 text-sm focus:outline-none focus:border-indigo-500"
                />
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-xs font-semibold text-slate-500 dark:text-slate-400 mb-1">{t('item.startLocal')}</label>
                  <input
                    type="datetime-local"
                    value={eventForm.start}
                    onChange={e => setEventForm({ ...eventForm, start: e.target.value })}
                    className="w-full bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-lg px-3 py-2 text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 text-sm focus:outline-none focus:border-indigo-500"
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-slate-500 dark:text-slate-400 mb-1">{t('item.endLocal')}</label>
                  <input
                    type="datetime-local"
                    value={eventForm.end}
                    onChange={e => setEventForm({ ...eventForm, end: e.target.value })}
                    className="w-full bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-lg px-3 py-2 text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 text-sm focus:outline-none focus:border-indigo-500"
                  />
                </div>
              </div>
              <div>
                <label className="block text-xs font-semibold text-slate-500 dark:text-slate-400 mb-1">{t('item.location')}</label>
                <input
                  type="text"
                  value={eventForm.location}
                  onChange={e => setEventForm({ ...eventForm, location: e.target.value })}
                  className="w-full bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-lg px-3 py-2 text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 text-sm focus:outline-none focus:border-indigo-500"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-slate-500 dark:text-slate-400 mb-1">{t('item.attendeesComma')}</label>
                <input
                  type="text"
                  value={eventForm.attendees}
                  onChange={e => setEventForm({ ...eventForm, attendees: e.target.value })}
                  placeholder="vd1@gmail.com, vd2@gmail.com"
                  className="w-full bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-lg px-3 py-2 text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 text-sm focus:outline-none focus:border-indigo-500"
                />
              </div>
              <div className="flex justify-end gap-2 pt-2">
                <button
                  onClick={() => setIsEditing(false)}
                  className="px-3.5 py-2 border border-slate-200 dark:border-slate-700 rounded-lg text-xs font-medium text-slate-600 dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-700 transition-colors"
                >
                  {t('common.cancel')}
                </button>
                <button
                  onClick={handleSaveEvent}
                  disabled={patchMutation.isPending}
                  className="px-3.5 py-2 bg-brand-600 text-white rounded-lg text-xs font-semibold hover:bg-brand-700 transition-colors flex items-center gap-1.5"
                >
                  {patchMutation.isPending && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
                  <span>{t('common.save')}</span>
                </button>
              </div>
            </div>
          ) : (
            /* Metadata Rows */
            rows.length > 0 && (
              <div className="border border-slate-200 dark:border-slate-800 rounded-[10px] overflow-hidden mb-[18px]">
                {rows.map((row, i) => (
                  <div key={i} className="flex gap-3 px-[13px] py-[9px] border-b border-slate-200 dark:border-slate-800 last:border-b-0">
                    <span className="text-[12.5px] text-slate-400 dark:text-slate-500 w-[118px] shrink-0">{row.label}</span>
                    <span className="text-[12.5px] text-slate-900 dark:text-slate-100 flex-1 break-words">{row.value}</span>
                  </div>
                ))}
              </div>
            )
          )}

          {/* Body Content */}
          <div className="text-[11px] font-semibold tracking-[0.04em] uppercase text-slate-400 dark:text-slate-500 mb-2">{t('sendEmail.content')}</div>
          <div className="text-[13.5px] text-slate-900 dark:text-slate-100 leading-[1.65] whitespace-pre-wrap bg-slate-50 dark:bg-slate-800 rounded-[10px] p-[14px]">
            {bodyText || <span className="text-slate-400 dark:text-slate-500 italic">{t('item.noContent')}</span>}
          </div>
        </div>

        {/* Footer actions — per type */}
        <div className="shrink-0 border-t border-slate-200 dark:border-slate-800 px-5 py-[14px] flex flex-wrap gap-2">
          {/* Chung cho Event/File/Note/Ticket: quan trọng (isImportant nội bộ) + đánh dấu chưa/đã xem (seenStore).
              Email có star/mark-read riêng qua Gmail nên loại trừ. */}
          {/* Quan trọng — dùng field isImportant NỘI BỘ của app cho MỌI loại (kể cả Email).
              KHÔNG ghi lên provider: đánh dấu quan trọng ở app KHÔNG động vào Gmail STARRED. */}
          <button
            onClick={() => importantMutation.mutate(!item.isImportant)}
            disabled={importantMutation.isPending}
            className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700 shadow-sm transition-colors"
          >
            <Star className={`w-4 h-4 ${item.isImportant ? 'fill-amber-400 text-amber-400' : 'text-slate-400 dark:text-slate-500'}`} />
            <span>{item.isImportant ? t('item.unmarkImportant') : t('item.markImportant')}</span>
          </button>

          {/* Đánh dấu chưa/đã xem — Email theo Gmail (write-back read state); còn lại theo seenStore (client). */}
          {item.type === 'Email' ? (
            <button
              onClick={() => patchMutation.mutate({ isUnread: !isUnread })}
              disabled={patchMutation.isPending}
              className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700 shadow-sm transition-colors"
            >
              {isUnread ? <Eye className="w-4 h-4 text-slate-500 dark:text-slate-400" /> : <EyeOff className="w-4 h-4 text-slate-500 dark:text-slate-400" />}
              <span>{isUnread ? t('item.markRead') : t('item.markUnread')}</span>
            </button>
          ) : (
            <button
              onClick={() => (unread ? markSeen(item.id) : markUnseen(item.id))}
              className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700 shadow-sm transition-colors"
            >
              {unread ? <Eye className="w-4 h-4 text-slate-500 dark:text-slate-400" /> : <EyeOff className="w-4 h-4 text-slate-500 dark:text-slate-400" />}
              <span>{unread ? t('item.markSeen') : t('item.markUnseen')}</span>
            </button>
          )}

          {item.type === 'Email' && (
            <>
              <button
                onClick={() => navigate('/scheduled')}
                className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-medium text-slate-600 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
              >
                <Send className="w-4 h-4 text-slate-400 dark:text-slate-500" /><span>{t('item.composeNew')}</span>
              </button>
              <button
                onClick={() => {
                  if (window.confirm(t('item.confirmDeleteEmail'))) {
                    deleteMutation.mutate();
                  }
                }}
                disabled={deleteMutation.isPending}
                className="w-[36px] h-[36px] inline-flex items-center justify-center rounded-lg bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/20 text-rose-600 dark:text-rose-400 hover:bg-rose-100 dark:hover:bg-rose-500/20 transition-colors"
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
                  className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-brand-600 text-white hover:bg-brand-700 shadow-sm transition-colors"
                >
                  <Edit3 className="w-4 h-4" /><span>{t('item.editEventBtn')}</span>
                </button>
              )}
              {metadata.meetUrl && (
                <a
                  href={metadata.meetUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700 shadow-sm transition-colors"
                >
                  <ExternalLink className="w-4 h-4 text-slate-500 dark:text-slate-400" /><span>Google Meet</span>
                </a>
              )}
              <button
                onClick={() => {
                  if (window.confirm(t('item.confirmDeleteEvent'))) {
                    deleteMutation.mutate();
                  }
                }}
                disabled={deleteMutation.isPending}
                className="w-[36px] h-[36px] inline-flex items-center justify-center rounded-lg bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/20 text-rose-600 dark:text-rose-400 hover:bg-rose-100 dark:hover:bg-rose-500/20 transition-colors"
              >
                {deleteMutation.isPending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Trash2 className="w-4 h-4" />}
              </button>
            </>
          )}

          {item.type === 'File' && (
            <>
              {canDriveShare && (
                <button
                  type="button"
                  onClick={() => setDriveShareOpen(true)}
                  className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700 shadow-sm"
                >
                  <Share2 className="w-4 h-4" />
                  <span>{t('item.share')}</span>
                </button>
              )}
              {fileIsDriveFolder && (
                <button
                  type="button"
                  onClick={() => setCreateSubfolderOpen(true)}
                  className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-medium text-slate-600 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800"
                >
                  <FolderPlus className="w-4 h-4" />
                  <span>{t('drive.createFolder.subfolder')}</span>
                </button>
              )}
              <button
                onClick={startRenamingFile}
                className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-brand-600 text-white hover:bg-brand-700 shadow-sm transition-colors"
              >
                <Edit3 className="w-4 h-4" /><span>{t('item.rename')}</span>
              </button>
              {metadata.webViewLink && (
                <a
                  href={metadata.webViewLink}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700 shadow-sm transition-colors"
                >
                  <ExternalLink className="w-4 h-4 text-slate-500 dark:text-slate-400" /><span>{t('item.openInDrive')}</span>
                </a>
              )}
              <button
                onClick={() => {
                  if (window.confirm(t('item.confirmDeleteFile'))) {
                    deleteMutation.mutate();
                  }
                }}
                disabled={deleteMutation.isPending}
                className="w-[36px] h-[36px] inline-flex items-center justify-center rounded-lg bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/20 text-rose-600 dark:text-rose-400 hover:bg-rose-100 dark:hover:bg-rose-500/20 transition-colors"
              >
                {deleteMutation.isPending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Trash2 className="w-4 h-4" />}
              </button>
            </>
          )}

          {item.type === 'Note' && (
            <button
              onClick={() => {
                if (window.confirm(t('item.confirmDeleteNote'))) {
                  deleteMutation.mutate();
                }
              }}
              disabled={deleteMutation.isPending}
              className="w-[36px] h-[36px] inline-flex items-center justify-center rounded-lg bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/20 text-rose-600 dark:text-rose-400 hover:bg-rose-100 dark:hover:bg-rose-500/20 transition-colors"
            >
              {deleteMutation.isPending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Trash2 className="w-4 h-4" />}
            </button>
          )}
        </div>

      </div>

      {/* slide-in animation */}
      <style>{`
        @keyframes wh-slide-in { from { transform: translateX(100%); } to { transform: translateX(0); } }
      `}</style>

      <TagManagerModal isOpen={tagManagerOpen} onClose={() => setTagManagerOpen(false)} />

      <DriveShareDialog
        itemId={itemId}
        itemTitle={item.title}
        isOpen={driveShareOpen}
        onClose={() => setDriveShareOpen(false)}
      />
      <CreateDriveFolderModal
        isOpen={createSubfolderOpen}
        onClose={() => setCreateSubfolderOpen(false)}
        defaultConnectionId={item.connectionId ?? undefined}
        defaultParentItemId={fileIsDriveFolder ? item.id : null}
      />
    </div>
  );
};
