/**
 * JiraTicketPanel — chi tiết ticket Jira, sửa NGAY tại field (không form).
 *  - Type/Priority/Assignee/Status: hiển thị luôn dạng dropdown, chọn là auto-save 2 chiều Jira.
 *  - Summary/Description: text, có nút "Sửa" ngay cạnh nội dung + highlight khi hover.
 *  - Comment: list + thêm/sửa/xoá. Attachment: list + tải/upload/xoá. (2 chiều)
 * Dùng trong ItemDetail khi item.type === 'Ticket'.
 */
import { useState, useRef, useEffect, useCallback } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import {
  Pencil, Check, X, Loader2, Trash2, Download, Paperclip,
  MessageSquare, UserRound, Upload, ChevronDown,
} from 'lucide-react';
import toast from 'react-hot-toast';
import { itemsApi, type JiraAttachment } from '../../lib/itemsApi';
import { jiraApi } from '../../lib/jiraApi';
import type { ItemResponse, PatchItemRequest } from '../../types/items';
import { handleApiError } from '../../lib/errorUtils';
import { useI18n } from '../../hooks/useI18n';
import { ConfirmDialog } from '../ConfirmDialog';
import { RichCommentBox } from './RichCommentBox';
import { renderRichText } from './miniMarkdown';

type TFn = ReturnType<typeof useI18n>['t'];
type Nav = ReturnType<typeof useNavigate>;
type Opt = { value: string; label: string; sub?: string };

interface Props {
  item: ItemResponse;
  metadata: Record<string, unknown>;
  /** Patch 2 chiều lên Jira (dùng chung patchMutation của ItemDetail — có conflict handling). */
  onPatch: (patch: PatchItemRequest) => Promise<unknown>;
  isPatching: boolean;
}

const INPUT =
  'w-full bg-white dark:bg-slate-800 border border-slate-300 dark:border-slate-700 rounded-lg px-2.5 py-1.5 text-[13px] text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 focus:outline-none focus:border-brand-500 dark:focus:border-brand-400 transition-colors';
const LABEL_COL = 'text-[13px] text-slate-400 dark:text-slate-500 w-[92px] shrink-0';

function fmtSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const kb = bytes / 1024;
  return kb < 1024 ? `${Math.round(kb)} KB` : `${(kb / 1024).toFixed(1)} MB`;
}

/** Dòng field chỉ đọc (Project / Issue Key / Due date). */
function ReadRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex gap-3 items-start">
      <span className={`${LABEL_COL} pt-0.5`}>{label}</span>
      <div className="flex-1 min-w-0 text-[13px] font-medium text-slate-800 dark:text-slate-100 break-words">{children}</div>
    </div>
  );
}

/** Dropdown auto-save cho field danh sách cố định (Type/Priority/Assignee/Status). */
function DropField({
  label, valueLabel, placeholder, icon, open, onOpen, onClose, options, loading, onSelect,
  saving, activeValue, disabled, searchable, searchValue, onSearch, searchPlaceholder,
}: {
  label: string; valueLabel: string; placeholder: string; icon?: React.ReactNode;
  open: boolean; onOpen: () => void; onClose: () => void;
  options: Opt[]; loading: boolean; onSelect: (value: string) => void;
  saving: boolean; activeValue?: string; disabled?: boolean;
  searchable?: boolean; searchValue?: string; onSearch?: (v: string) => void; searchPlaceholder?: string;
}) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const h = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) onClose(); };
    const k = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('mousedown', h);
    document.addEventListener('keydown', k);
    return () => { document.removeEventListener('mousedown', h); document.removeEventListener('keydown', k); };
  }, [open, onClose]);

  return (
    <div className="flex gap-3 items-center">
      <span className={LABEL_COL}>{label}</span>
      <div className="relative w-full max-w-[240px] min-w-0" ref={ref}>
        <button
          type="button"
          disabled={disabled || saving}
          onClick={() => (open ? onClose() : onOpen())}
          className={`w-full flex items-center justify-between gap-2 px-2.5 py-1 rounded-md text-[13px] bg-white dark:bg-slate-800 border transition-colors disabled:opacity-60 ${open ? 'border-brand-500 ring-2 ring-brand-500/20' : 'border-slate-200 dark:border-slate-700 hover:border-slate-300 dark:hover:border-slate-600'}`}
        >
          <span className={`min-w-0 flex items-center gap-1.5 truncate ${valueLabel ? 'text-slate-800 dark:text-slate-100 font-medium' : 'text-slate-400'}`}>
            {icon}<span className="truncate">{valueLabel || placeholder}</span>
          </span>
          {saving ? <Loader2 className="w-3.5 h-3.5 animate-spin text-brand-500 shrink-0" /> : <ChevronDown className={`w-3.5 h-3.5 text-slate-400 shrink-0 transition-transform ${open ? 'rotate-180' : ''}`} />}
        </button>
        {open && (
          <div className="absolute z-[70] mt-1 w-full min-w-max bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl shadow-xl py-1 max-h-60 overflow-auto">
            {searchable && (
              <div className="px-2 pb-1">
                <input
                  autoFocus type="text" value={searchValue ?? ''}
                  onChange={(e) => onSearch?.(e.target.value)}
                  placeholder={searchPlaceholder}
                  className="w-full bg-slate-50 dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-md px-2 py-1 text-[12.5px] focus:outline-none focus:border-brand-500"
                />
              </div>
            )}
            {loading ? (
              <div className="flex items-center gap-2 px-3 py-2 text-[12px] text-slate-500"><Loader2 className="w-3.5 h-3.5 animate-spin" />…</div>
            ) : options.length === 0 ? (
              <div className="px-3 py-2 text-[12px] text-slate-400">—</div>
            ) : options.map((o) => (
              <button
                key={o.value || '__none'}
                type="button"
                onClick={() => onSelect(o.value)}
                className={`w-full flex items-center justify-between gap-3 px-3 py-1.5 text-[12.5px] text-left transition-colors ${o.value === activeValue ? 'bg-brand-50 text-brand-700 font-medium dark:bg-brand-500/15 dark:text-brand-300' : 'text-slate-700 hover:bg-slate-50 dark:text-slate-200 dark:hover:bg-slate-700'}`}
              >
                <span className="truncate">{o.label}{o.sub && <span className="ml-1 text-slate-400 text-[11px]">{o.sub}</span>}</span>
                {o.value === activeValue && <Check className="w-3.5 h-3.5 shrink-0" />}
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

export function JiraTicketPanel({ item, metadata, onPatch, isPatching }: Props) {
  const { t, lang } = useI18n();
  const dl = lang === 'vi' ? 'vi-VN' : 'en-US';
  const navigate = useNavigate();
  const qc = useQueryClient();
  const itemId = item.id;
  const connectionId = (item.connectionId ?? '') as string;
  const projectKey = (metadata.projectKey as string) ?? '';

  const [openDrop, setOpenDrop] = useState<'type' | 'priority' | 'assignee' | 'status' | null>(null);
  const [editingText, setEditingText] = useState<'summary' | 'description' | null>(null);
  const [draft, setDraft] = useState('');

  const curSummary = item.title ?? '';
  const curDescription = (metadata.description as string) ?? item.snippet ?? '';
  const curPriority = (metadata.priority as string) ?? '';
  const curAssignee = (metadata.assignee as string) ?? '';
  const curAssigneeId = (metadata.assigneeAccountId as string) ?? '';
  const curStatus = (metadata.status as string) ?? '';
  const curType = (metadata.issueType as string) ?? '';

  const savePatch = async (patch: PatchItemRequest) => {
    try {
      await onPatch(patch);
      setOpenDrop(null); setEditingText(null);
      // Đổi status → transition khả dụng thay đổi → làm mới cache transitions.
      if (patch.statusTransition) qc.invalidateQueries({ queryKey: ['jira', 'transitions', connectionId, itemId] });
    }
    catch { /* lỗi đã toast ở patchMutation.onError */ }
  };
  const editText = (field: 'summary' | 'description', initial: string) => { setEditingText(field); setDraft(initial); };
  const cancelText = () => { setEditingText(null); setDraft(''); };

  // ── Option queries — PREFETCH ngay khi mở ticket + cache (staleTime) để bấm dropdown là hiện liền.
  //    (Trước đây chỉ fetch khi mở dropdown + staleTime 0 → mỗi lần bấm phải chờ gọi lại API.)
  const { data: issueTypes = [], isLoading: loadingTypes } = useQuery({
    queryKey: ['jira', 'issueTypes', connectionId, projectKey],
    queryFn: () => jiraApi.getIssueTypes(connectionId, projectKey),
    enabled: !!connectionId && !!projectKey,
    staleTime: 5 * 60_000,
  });
  const { data: priorities = [], isLoading: loadingPriorities } = useQuery({
    queryKey: ['jira', 'priorities', connectionId],
    queryFn: () => jiraApi.getPriorities(connectionId),
    enabled: !!connectionId,
    staleTime: 5 * 60_000,
  });
  const { data: transitions = [], isLoading: loadingTransitions } = useQuery({
    queryKey: ['jira', 'transitions', connectionId, itemId],
    queryFn: () => jiraApi.getTransitions(connectionId, itemId),
    enabled: !!connectionId && !!itemId,
    staleTime: 60_000, // cache 1' — sau khi đổi status sẽ invalidate ở savePatch để lấy transition mới
  });

  // Assignee: search tách khỏi giá trị đang chọn
  const [assigneeSearch, setAssigneeSearch] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const onSearchChange = useCallback((val: string) => {
    setAssigneeSearch(val);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => setDebouncedQuery(val), 300);
  }, []);
  // Danh sách base (query rỗng) prefetch + cache; gõ tìm thì mới gọi thêm theo từ khoá.
  const { data: assignableUsers = [], isLoading: loadingAssignees } = useQuery({
    queryKey: ['jira', 'assignableUsers', connectionId, projectKey, debouncedQuery],
    queryFn: () => jiraApi.getAssignableUsers(connectionId, projectKey, debouncedQuery),
    enabled: !!connectionId && !!projectKey,
    staleTime: 5 * 60_000,
  });

  const patchingType = isPatching && openDrop === 'type';
  const patchingPrio = isPatching && openDrop === 'priority';
  const patchingAssignee = isPatching && openDrop === 'assignee';
  const patchingStatus = isPatching && openDrop === 'status';

  return (
    <div className="space-y-5">
      {/* ─────────── Thông tin ─────────── */}
      <div className="rounded-xl bg-slate-50 dark:bg-slate-800/40 px-4 py-3.5 space-y-2.5">
        {/* Summary — text, nút Sửa ngay cạnh nội dung + highlight hover */}
        {editingText === 'summary' ? (
          <div className="flex gap-3 items-start">
            <span className={`${LABEL_COL} pt-1.5`}>{t('ticket.summaryLabel')}</span>
            <div className="flex-1 min-w-0 flex items-start gap-1.5">
              <input
                autoFocus type="text" value={draft} maxLength={255}
                onChange={(e) => setDraft(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') savePatch({ summary: draft.trim() }); else if (e.key === 'Escape') cancelText(); }}
                className={INPUT}
              />
              <InlineSaveCancel onSave={() => savePatch({ summary: draft.trim() })} onCancel={cancelText} disabled={!draft.trim() || draft.trim() === curSummary} saving={isPatching} />
            </div>
          </div>
        ) : (
          <div className="flex gap-3 items-start">
            <span className={`${LABEL_COL} pt-1.5`}>{t('ticket.summaryLabel')}</span>
            <EditableText text={curSummary} onEdit={() => editText('summary', curSummary)} editLabel={t('ticket.editField')} />
          </div>
        )}

        {/* Project / Issue key — read-only */}
        {(metadata.projectName || metadata.projectKey) != null && (
          <ReadRow label="Project">{metadata.projectName ? `${metadata.projectName} (${metadata.projectKey})` : String(metadata.projectKey)}</ReadRow>
        )}
        {metadata.issueKey != null && <ReadRow label="Issue Key">{String(metadata.issueKey)}</ReadRow>}

        {/* Type — dropdown auto-save */}
        <DropField
          label={t('item.issueType')}
          valueLabel={curType}
          placeholder="—"
          open={openDrop === 'type'}
          onOpen={() => setOpenDrop('type')}
          onClose={() => setOpenDrop(null)}
          loading={loadingTypes}
          saving={patchingType}
          activeValue={curType}
          disabled={!projectKey}
          options={issueTypes.filter((it) => !it.subtask).map((it) => ({ value: it.name, label: it.name }))}
          onSelect={(v) => { if (v !== curType) savePatch({ issueType: v }); else setOpenDrop(null); }}
        />

        {/* Priority — dropdown auto-save */}
        <DropField
          label={t('ticket.priorityLabel')}
          valueLabel={curPriority}
          placeholder={t('ticket.noPriority')}
          open={openDrop === 'priority'}
          onOpen={() => setOpenDrop('priority')}
          onClose={() => setOpenDrop(null)}
          loading={loadingPriorities}
          saving={patchingPrio}
          activeValue={curPriority}
          options={priorities.map((p) => ({ value: p.name, label: p.name }))}
          onSelect={(v) => { if (v !== curPriority) savePatch({ priority: v }); else setOpenDrop(null); }}
        />

        {/* Assignee — dropdown search auto-save */}
        <DropField
          label={t('ticket.assigneeLabel')}
          valueLabel={curAssignee}
          placeholder={t('createTicket.noAssignee')}
          icon={<UserRound className="w-3.5 h-3.5 text-slate-400 shrink-0" />}
          open={openDrop === 'assignee'}
          onOpen={() => { setAssigneeSearch(''); setDebouncedQuery(''); setOpenDrop('assignee'); }}
          onClose={() => setOpenDrop(null)}
          loading={loadingAssignees}
          saving={patchingAssignee}
          activeValue={curAssigneeId}
          disabled={!projectKey}
          searchable searchValue={assigneeSearch} onSearch={onSearchChange} searchPlaceholder={t('ticket.assigneeSearchPlaceholder')}
          options={[
            { value: '', label: t('createTicket.noAssignee') },
            ...assignableUsers.map((u) => ({ value: u.accountId, label: u.displayName, sub: u.email ?? undefined })),
          ]}
          onSelect={(v) => { if (v !== curAssigneeId) savePatch({ assignee: v }); else setOpenDrop(null); }}
        />

        {/* Status — dropdown transition auto-save (hiển thị status hiện tại, chọn transition để đổi) */}
        <DropField
          label={t('item.status')}
          valueLabel={curStatus}
          placeholder="—"
          open={openDrop === 'status'}
          onOpen={() => setOpenDrop('status')}
          onClose={() => setOpenDrop(null)}
          loading={loadingTransitions}
          saving={patchingStatus}
          options={transitions.map((tr) => ({ value: tr.id, label: tr.name, sub: tr.toStatusName ? `→ ${tr.toStatusName}` : undefined }))}
          onSelect={(v) => { if (v) savePatch({ statusTransition: v }); }}
        />

        {item.dueAt && <ReadRow label="Due date">{new Date(item.dueAt).toLocaleString(dl)}</ReadRow>}
      </div>

      {/* ─────────── Description ─────────── */}
      <div>
        <span className="block text-[11px] font-semibold tracking-[0.04em] uppercase text-slate-400 dark:text-slate-500 mb-2">{t('ticket.descLabel')}</span>
        {editingText === 'description' ? (
          /* Sửa mô tả bằng RichCommentBox — cùng toolbar định dạng + preview như comment (markdown subset ↔ ADF). */
          <RichCommentBox
            initialValue={curDescription}
            placeholder={t('ticket.descEmpty')}
            submitLabel={t('common.save')}
            pending={isPatching}
            t={t}
            onSubmit={(body) => savePatch({ description: body })}
            onCancel={cancelText}
          />
        ) : (
          <button
            onClick={() => editText('description', curDescription)}
            className="group relative w-full text-left rounded-xl bg-slate-50 dark:bg-slate-800/60 ring-1 ring-slate-200/70 dark:ring-slate-700/60 hover:bg-brand-50/40 dark:hover:bg-slate-700/50 hover:ring-brand-200 dark:hover:ring-brand-500/30 px-4 py-3.5 transition-colors"
          >
            {/* Render markdown subset (đậm/nghiêng/list/link) — cùng renderer với comment, khớp ADF từ Jira */}
            <div className="text-[13.5px] text-slate-900 dark:text-slate-100 leading-[1.65] break-words">
              {curDescription
                ? renderRichText(curDescription)
                : <span className="text-slate-400 dark:text-slate-500 italic">{t('ticket.descEmpty')}</span>}
            </div>
            <span className="absolute top-2 right-2 opacity-0 group-hover:opacity-100 transition-opacity inline-flex items-center gap-1 px-1.5 py-0.5 rounded bg-white dark:bg-slate-900 border border-brand-200 dark:border-brand-500/40 text-brand-600 dark:text-brand-400 text-[11px] font-medium shadow-sm">
              <Pencil className="w-3 h-3" />{t('ticket.editField')}
            </span>
          </button>
        )}
      </div>

      {/* ─────────── Attachments ─────────── */}
      <AttachmentsSection itemId={itemId} t={t} dl={dl} navigate={navigate} />

      {/* ─────────── Comments ─────────── */}
      <CommentsSection itemId={itemId} t={t} dl={dl} navigate={navigate} />
    </div>
  );
}

/** Text hiển thị + nút "Sửa" NGAY cạnh nội dung; cả vùng highlight khi hover. */
function EditableText({ text, onEdit, editLabel }: { text: string; onEdit: () => void; editLabel: string }) {
  return (
    <button
      onClick={onEdit}
      className="group flex-1 min-w-0 flex items-center gap-2 text-left rounded-lg px-1.5 py-1 -mx-1.5 hover:bg-brand-50/60 dark:hover:bg-slate-700/60 transition-colors"
    >
      <span className="min-w-0 text-[13px] font-medium text-slate-800 dark:text-slate-100 break-words">{text}</span>
      <span className="shrink-0 opacity-0 group-hover:opacity-100 transition-opacity inline-flex items-center gap-0.5 text-brand-600 dark:text-brand-400 text-[11px] font-medium">
        <Pencil className="w-3 h-3" />{editLabel}
      </span>
    </button>
  );
}

function InlineSaveCancel({ onSave, onCancel, disabled, saving }: { onSave: () => void; onCancel: () => void; disabled?: boolean; saving: boolean }) {
  return (
    <div className="flex items-center gap-1 shrink-0">
      <button onClick={onSave} disabled={disabled || saving} className="p-1.5 rounded-lg bg-brand-600 text-white hover:bg-brand-700 disabled:opacity-60 transition-colors">
        {saving ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Check className="w-3.5 h-3.5" />}
      </button>
      <button onClick={onCancel} disabled={saving} className="p-1.5 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-700 transition-colors">
        <X className="w-3.5 h-3.5" />
      </button>
    </div>
  );
}

// ═══════════════════════ Attachments ═══════════════════════
function AttachmentsSection({ itemId, t, dl, navigate }: { itemId: string; t: TFn; dl: string; navigate: Nav }) {
  const qc = useQueryClient();
  const fileRef = useRef<HTMLInputElement>(null);
  const [confirmId, setConfirmId] = useState<string | null>(null);

  const { data: attachments = [], isLoading } = useQuery({
    queryKey: ['ticket', 'attachments', itemId],
    queryFn: () => itemsApi.getAttachments(itemId),
  });

  const uploadMut = useMutation({
    mutationFn: (file: File) => itemsApi.uploadAttachment(itemId, file),
    onSuccess: () => { toast.success(t('ticket.attachmentUploaded')); qc.invalidateQueries({ queryKey: ['ticket', 'attachments', itemId] }); },
    onError: (err) => handleApiError(err, t('ticket.attachmentFail'), { navigate }),
  });
  const deleteMut = useMutation({
    mutationFn: (attId: string) => itemsApi.deleteAttachment(itemId, attId),
    onSuccess: () => { toast.success(t('ticket.attachmentDeleted')); qc.invalidateQueries({ queryKey: ['ticket', 'attachments', itemId] }); },
    onError: (err) => handleApiError(err, t('ticket.attachmentFail'), { navigate }),
  });
  const [downloadingId, setDownloadingId] = useState<string | null>(null);
  const download = async (a: JiraAttachment) => {
    setDownloadingId(a.id);
    try { await itemsApi.downloadAttachment(itemId, a.id, a.filename); }
    catch (err) { handleApiError(err, t('ticket.attachmentFail'), { navigate }); }
    finally { setDownloadingId(null); }
  };

  const onPickFile = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;
    if (file.size > 25 * 1024 * 1024) { toast.error(t('ticket.attachmentTooBig')); return; }
    uploadMut.mutate(file);
  };

  return (
    <div>
      <div className="flex items-center justify-between mb-2">
        <span className="inline-flex items-center gap-1.5 text-[11px] font-semibold tracking-[0.04em] uppercase text-slate-400 dark:text-slate-500">
          <Paperclip className="w-3.5 h-3.5" />{t('ticket.attachmentsCount', { count: attachments.length })}
        </span>
        <button
          onClick={() => fileRef.current?.click()}
          disabled={uploadMut.isPending}
          className="inline-flex items-center gap-1 text-[12px] font-medium text-brand-600 dark:text-brand-400 hover:text-brand-700 disabled:opacity-60 transition-colors"
        >
          {uploadMut.isPending ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Upload className="w-3.5 h-3.5" />}
          {uploadMut.isPending ? t('ticket.attachmentUploading') : t('ticket.attachmentUpload')}
        </button>
        <input ref={fileRef} type="file" className="hidden" onChange={onPickFile} />
      </div>

      {isLoading ? (
        <div className="flex items-center gap-2 text-[12.5px] text-slate-500 py-2"><Loader2 className="w-3.5 h-3.5 animate-spin" />{t('ticket.loadingAttachments')}</div>
      ) : attachments.length === 0 ? (
        <p className="text-[12.5px] text-slate-400 dark:text-slate-500 italic py-1">{t('ticket.attachmentEmpty')}</p>
      ) : (
        <div className="space-y-1.5">
          {attachments.map((a) => (
            <div key={a.id} className="flex items-center gap-2.5 px-3 py-2 rounded-lg bg-slate-50 dark:bg-slate-800/60 border border-slate-100 dark:border-slate-700/60">
              {a.mimeType?.startsWith('image/') ? (
                <img src={itemsApi.attachmentUrl(itemId, a.id)} alt={a.filename} loading="lazy" className="w-10 h-10 rounded object-cover border border-slate-200 dark:border-slate-700 shrink-0" />
              ) : (
                <Paperclip className="w-4 h-4 text-slate-400 shrink-0" />
              )}
              <div className="flex-1 min-w-0">
                <div className="text-[13px] font-medium text-slate-800 dark:text-slate-100 truncate">{a.filename}</div>
                <div className="text-[11px] text-slate-400">{fmtSize(a.size)}{a.created ? ` · ${new Date(a.created).toLocaleDateString(dl)}` : ''}</div>
              </div>
              <button onClick={() => download(a)} disabled={downloadingId === a.id} title={t('ticket.attachmentDownload')} className="p-1.5 rounded-lg text-slate-500 hover:text-brand-600 hover:bg-white dark:hover:bg-slate-700 transition-colors">
                {downloadingId === a.id ? <Loader2 className="w-4 h-4 animate-spin" /> : <Download className="w-4 h-4" />}
              </button>
              <button onClick={() => setConfirmId(a.id)} title={t('ticket.attachmentDelete')} className="p-1.5 rounded-lg text-slate-400 hover:text-rose-600 hover:bg-white dark:hover:bg-slate-700 transition-colors">
                <Trash2 className="w-4 h-4" />
              </button>
            </div>
          ))}
        </div>
      )}

      <ConfirmDialog
        open={confirmId !== null}
        tone="danger"
        message={t('ticket.attachmentDeleteConfirm')}
        confirmLabel={t('common.delete')}
        loading={deleteMut.isPending}
        onConfirm={() => { if (confirmId) deleteMut.mutate(confirmId, { onSuccess: () => setConfirmId(null), onError: () => setConfirmId(null) }); }}
        onCancel={() => setConfirmId(null)}
      />
    </div>
  );
}

// ═══════════════════════ Comments ═══════════════════════
function CommentsSection({ itemId, t, dl, navigate }: { itemId: string; t: TFn; dl: string; navigate: Nav }) {
  const qc = useQueryClient();
  const [editingId, setEditingId] = useState<string | null>(null);
  const [confirmId, setConfirmId] = useState<string | null>(null);

  const { data: comments = [], isLoading } = useQuery({
    queryKey: ['ticket', 'comments', itemId],
    queryFn: () => itemsApi.getComments(itemId),
  });
  // Attachments để resolve marker [[attach:id]] → tên file + tải xuống.
  const { data: attachments = [] } = useQuery({
    queryKey: ['ticket', 'attachments', itemId],
    queryFn: () => itemsApi.getAttachments(itemId),
  });
  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['ticket', 'comments', itemId] });
    qc.invalidateQueries({ queryKey: ['ticket', 'attachments', itemId] });
  };

  // Thêm comment: upload file (nếu có) → lấy id → addComment(body, mediaIds).
  const addMut = useMutation({
    mutationFn: async ({ body, files }: { body: string; files: File[] }) => {
      const mediaIds: string[] = [];
      for (const f of files) {
        const created = await itemsApi.uploadAttachment(itemId, f);
        mediaIds.push(...created.map((a) => a.id));
      }
      return itemsApi.addComment(itemId, body, mediaIds.length ? mediaIds : undefined);
    },
    onSuccess: () => { toast.success(t('ticket.commentAdded')); invalidate(); },
    onError: (err) => handleApiError(err, t('ticket.commentFail'), { navigate }),
  });
  const updateMut = useMutation({
    mutationFn: ({ id, body }: { id: string; body: string }) => itemsApi.updateComment(itemId, id, body),
    onSuccess: () => { toast.success(t('ticket.commentUpdated')); setEditingId(null); invalidate(); },
    onError: (err) => handleApiError(err, t('ticket.commentFail'), { navigate }),
  });
  const deleteMut = useMutation({
    mutationFn: (id: string) => itemsApi.deleteComment(itemId, id),
    onSuccess: () => { toast.success(t('ticket.commentDeleted')); invalidate(); },
    onError: (err) => handleApiError(err, t('ticket.commentFail'), { navigate }),
  });

  const [downloadingId, setDownloadingId] = useState<string | null>(null);
  const downloadAttach = async (id: string, filename: string) => {
    setDownloadingId(id);
    try { await itemsApi.downloadAttachment(itemId, id, filename); }
    catch (err) { handleApiError(err, t('ticket.attachmentFail'), { navigate }); }
    finally { setDownloadingId(null); }
  };
  // Chip / preview cho marker [[attach:id]] trong body comment.
  const attachChip = (id: string) => {
    const a = attachments.find((x) => x.id === id);
    // File không còn trên issue (đã xoá / marker cũ hỏng) → chip vô hiệu, không cho bấm (tránh 404).
    if (!a) {
      return (
        <span className="inline-flex items-center gap-1 mx-0.5 pl-1.5 pr-2 py-0.5 rounded-md bg-slate-50 dark:bg-slate-800 border border-dashed border-slate-300 dark:border-slate-600 text-[12px] text-slate-400 align-middle" title={t('ticket.attachmentGone')}>
          <Paperclip className="w-3 h-3" />{t('ticket.attachmentGone')}
        </span>
      );
    }
    // Ảnh → preview thumbnail (bấm để tải bản gốc). <img> cùng origin nên cookie auth tự gửi.
    if (a.mimeType?.startsWith('image/')) {
      return (
        <span className="block my-1.5">
          <button onClick={() => downloadAttach(id, a.filename)} disabled={downloadingId === id} title={a.filename} className="group inline-block relative rounded-lg overflow-hidden border border-slate-200 dark:border-slate-700 hover:border-brand-400 transition-colors max-w-[240px]">
            <img src={itemsApi.attachmentUrl(itemId, id)} alt={a.filename} loading="lazy" className="block max-h-48 max-w-full object-cover" />
            <span className="absolute bottom-0 inset-x-0 flex items-center gap-1 px-2 py-1 bg-black/45 text-white text-[11px] opacity-0 group-hover:opacity-100 transition-opacity">
              {downloadingId === id ? <Loader2 className="w-3 h-3 animate-spin" /> : <Download className="w-3 h-3" />}<span className="truncate">{a.filename}</span>
            </span>
          </button>
        </span>
      );
    }
    return (
      <button
        onClick={() => downloadAttach(id, a.filename)}
        disabled={downloadingId === id}
        className="inline-flex items-center gap-1 mx-0.5 pl-1.5 pr-2 py-0.5 rounded-md bg-white dark:bg-slate-700 border border-slate-200 dark:border-slate-600 text-[12px] text-slate-700 dark:text-slate-200 hover:border-brand-400 align-middle transition-colors"
      >
        {downloadingId === id ? <Loader2 className="w-3 h-3 animate-spin" /> : <Download className="w-3 h-3 text-slate-400" />}{a.filename}
      </button>
    );
  };

  return (
    <div>
      <span className="inline-flex items-center gap-1.5 text-[11px] font-semibold tracking-[0.04em] uppercase text-slate-400 dark:text-slate-500 mb-2">
        <MessageSquare className="w-3.5 h-3.5" />{t('ticket.commentsCount', { count: comments.length })}
      </span>

      {/* Ô thêm comment — rich toolbar + đính kèm */}
      <div className="mb-3">
        <RichCommentBox
          placeholder={t('ticket.commentAddPlaceholder')}
          submitLabel={t('ticket.commentSend')}
          allowAttach
          pending={addMut.isPending}
          t={t}
          onSubmit={(body, files) => addMut.mutateAsync({ body, files })}
        />
      </div>

      {isLoading ? (
        <div className="flex items-center gap-2 text-[12.5px] text-slate-500 py-2"><Loader2 className="w-3.5 h-3.5 animate-spin" />{t('ticket.loadingComments')}</div>
      ) : comments.length === 0 ? (
        <p className="text-[12.5px] text-slate-400 dark:text-slate-500 italic py-1">{t('ticket.commentEmpty')}</p>
      ) : (
        <div className="space-y-2.5">
          {comments.map((c) => (
            <div key={c.id} className="rounded-lg bg-slate-50 dark:bg-slate-800/60 border border-slate-100 dark:border-slate-700/60 px-3 py-2.5 group">
              <div className="flex items-center justify-between mb-1">
                <span className="text-[12.5px] font-semibold text-slate-700 dark:text-slate-200">{c.authorName}</span>
                <div className="flex items-center gap-2">
                  {(c.updated || c.created) && (
                    <span className="text-[11px] text-slate-400">{new Date((c.updated || c.created)!).toLocaleString(dl)}</span>
                  )}
                  {editingId !== c.id && (
                    <div className="flex items-center gap-0.5 opacity-0 group-hover:opacity-100 transition-opacity">
                      <button onClick={() => setEditingId(c.id)} title={t('ticket.commentEdit')} className="p-1 rounded text-slate-400 hover:text-brand-600 hover:bg-white dark:hover:bg-slate-700 transition-colors"><Pencil className="w-3.5 h-3.5" /></button>
                      <button onClick={() => setConfirmId(c.id)} title={t('ticket.commentDelete')} className="p-1 rounded text-slate-400 hover:text-rose-600 hover:bg-white dark:hover:bg-slate-700 transition-colors"><Trash2 className="w-3.5 h-3.5" /></button>
                    </div>
                  )}
                </div>
              </div>
              {editingId === c.id ? (
                <RichCommentBox
                  initialValue={c.body}
                  placeholder={t('ticket.commentAddPlaceholder')}
                  submitLabel={t('ticket.commentSave')}
                  pending={updateMut.isPending}
                  t={t}
                  onSubmit={(body) => updateMut.mutateAsync({ id: c.id, body })}
                  onCancel={() => setEditingId(null)}
                />
              ) : (
                <div className="text-[13px] text-slate-800 dark:text-slate-100 leading-[1.55] break-words">
                  {renderRichText(c.body, attachChip)}
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      <ConfirmDialog
        open={confirmId !== null}
        tone="danger"
        message={t('ticket.commentDeleteConfirm')}
        confirmLabel={t('common.delete')}
        loading={deleteMut.isPending}
        onConfirm={() => { if (confirmId) deleteMut.mutate(confirmId, { onSuccess: () => setConfirmId(null), onError: () => setConfirmId(null) }); }}
        onCancel={() => setConfirmId(null)}
      />
    </div>
  );
}
