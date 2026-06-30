import type { ItemResponse } from '../types/items';
import {
  X, Mail, Calendar, FileText, StickyNote, Briefcase,
  Trash, ExternalLink, Eye, Star, Tag, Send, Save, Reply,
} from 'lucide-react';

interface ItemDetailProps {
  item: ItemResponse | null;
  onClose: () => void;
  onToggleImportant?: (id: string, isImportant: boolean) => void;
}

const STATUS_LABEL: Record<string, string> = { Inbox: 'Cần xem', Doing: 'Đang xử lý', Done: 'Done' };
const STATUS_COLOR: Record<string, string> = {
  Inbox: 'bg-slate-100 text-slate-600',
  Doing: 'bg-blue-50 text-blue-700',
  Done: 'bg-emerald-50 text-emerald-700',
};
const STATUS_DOT: Record<string, string> = { Inbox: 'bg-slate-400', Doing: 'bg-blue-500', Done: 'bg-emerald-500' };

const TYPE_INFO: Record<string, { label: string; icon: React.ReactNode; bg: string }> = {
  Email:  { label: 'Email',    icon: <Mail className="w-5 h-5" />,      bg: 'bg-blue-50 text-blue-600' },
  Event:  { label: 'Sự kiện', icon: <Calendar className="w-5 h-5" />,   bg: 'bg-amber-50 text-amber-600' },
  File:   { label: 'Tệp',     icon: <FileText className="w-5 h-5" />,   bg: 'bg-emerald-50 text-emerald-600' },
  Note:   { label: 'Ghi chú', icon: <StickyNote className="w-5 h-5" />, bg: 'bg-slate-100 text-slate-500' },
  Ticket: { label: 'Ticket',  icon: <Briefcase className="w-5 h-5" />,  bg: 'bg-purple-50 text-purple-600' },
};

export function ItemDetail({ item, onClose, onToggleImportant }: ItemDetailProps) {
  if (!item) return null;

  const meta = (() => {
    try { return item.metadataJson ? JSON.parse(item.metadataJson) : {}; }
    catch { return {}; }
  })();

  const tInfo = TYPE_INFO[item.type] ?? TYPE_INFO.Note;
  const statusLabel = STATUS_LABEL[item.status] ?? item.status;
  const statusColor = STATUS_COLOR[item.status] ?? 'bg-slate-100 text-slate-500';
  const statusDot = STATUS_DOT[item.status] ?? 'bg-slate-400';

  const typeChip = `inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[11.5px] font-semibold ${tInfo.bg}`;

  // ── metadata rows per type
  const rows: { label: string; value: string }[] = [];
  if (item.type === 'Email') {
    if (meta.from)  rows.push({ label: 'Từ',   value: meta.from });
    const to = Array.isArray(meta.to) ? meta.to.join(', ') : meta.to;
    if (to)         rows.push({ label: 'Đến',  value: to });
    if (meta.labels?.length) rows.push({ label: 'Nhãn', value: meta.labels.filter((l: string) => l !== 'INBOX').join(', ') || meta.labels.join(', ') });
    rows.push({ label: 'Thời gian', value: new Date(item.occurredAt).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' }) });
  } else if (item.type === 'Event') {
    rows.push({ label: 'Bắt đầu', value: new Date(item.occurredAt).toLocaleString('vi-VN') });
    if (item.dueAt) rows.push({ label: 'Kết thúc', value: new Date(item.dueAt).toLocaleString('vi-VN') });
    if (meta.location) rows.push({ label: 'Địa điểm', value: meta.location });
    if (Array.isArray(meta.attendees) && meta.attendees.length)
      rows.push({ label: 'Người tham gia', value: meta.attendees.join(', ') });
  } else if (item.type === 'File') {
    rows.push({ label: 'Được tạo', value: new Date(item.occurredAt).toLocaleString('vi-VN') });
    if (meta.mimeType) rows.push({ label: 'Loại tệp', value: meta.mimeType });
    if (meta.size) rows.push({ label: 'Kích thước', value: `${Math.round(meta.size / 1024)} KB` });
  } else if (item.type === 'Note') {
    rows.push({ label: 'Được tạo', value: new Date(item.occurredAt).toLocaleString('vi-VN') });
  } else if (item.type === 'Ticket') {
    if (meta.issueKey)   rows.push({ label: 'Issue Key',  value: meta.issueKey });
    if (meta.projectKey) rows.push({ label: 'Project',    value: meta.projectKey });
    if (meta.issueType)  rows.push({ label: 'Loại',       value: meta.issueType });
    if (meta.priority)   rows.push({ label: 'Ưu tiên',    value: meta.priority });
    if (meta.assignee)   rows.push({ label: 'Assignee',   value: meta.assignee });
    if (meta.reporter)   rows.push({ label: 'Reporter',   value: meta.reporter });
    if (meta.status)     rows.push({ label: 'Trạng thái', value: meta.status });
    if (Array.isArray(meta.labels) && meta.labels.length)
      rows.push({ label: 'Labels', value: meta.labels.join(', ') });
    if (item.dueAt) rows.push({ label: 'Due date', value: new Date(item.dueAt).toLocaleString('vi-VN') });
  }

  // body text
  const bodyText: string =
    meta.body ?? meta.description ?? meta.contentMarkdown ?? item.snippet ?? '';

  return (
    <div className="fixed inset-0 z-50 flex justify-end">
      {/* Backdrop */}
      <div onClick={onClose} className="absolute inset-0 bg-slate-900/40" />

      {/* Drawer */}
      <div className="relative w-full max-w-[462px] bg-white border-l border-slate-200 shadow-2xl flex flex-col" style={{ animation: 'wh-slide-in .25s ease' }}>

        {/* Header */}
        <div className="flex items-start gap-3 px-5 py-[18px] border-b border-slate-200 shrink-0">
          <div className={`w-[42px] h-[42px] rounded-xl flex items-center justify-center shrink-0 ${tInfo.bg}`}>
            {tInfo.icon}
          </div>
          <div className="flex-1 min-w-0 pt-0.5">
            <div className="flex flex-wrap gap-[6px] mb-[7px]">
              <span className={typeChip}>{tInfo.label}</span>
              <span className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[11.5px] font-semibold ${statusColor}`}>
                <span className={`w-1.5 h-1.5 rounded-full ${statusDot}`} />
                {statusLabel}
              </span>
            </div>
            <h2 className="text-[17px] font-semibold text-slate-900 leading-[1.4] m-0">{item.title}</h2>
          </div>
          <button onClick={onClose} className="p-1.5 text-slate-400 hover:text-slate-700 hover:bg-slate-100 rounded-lg shrink-0 transition-colors">
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto px-5 py-[18px]">

          {/* Metadata rows */}
          {rows.length > 0 && (
            <div className="border border-slate-200 rounded-[10px] overflow-hidden mb-[18px]">
              {rows.map((row, i) => (
                <div key={i} className="flex gap-3 px-[13px] py-[9px] border-b border-slate-200 last:border-b-0">
                  <span className="text-[12.5px] text-slate-400 w-[118px] shrink-0">{row.label}</span>
                  <span className="text-[12.5px] text-slate-900 flex-1 break-words">{row.value}</span>
                </div>
              ))}
            </div>
          )}

          {/* Body */}
          <div className="text-[11px] font-semibold tracking-[0.04em] uppercase text-slate-400 mb-2">Nội dung</div>
          <div className="text-[13.5px] text-slate-900 leading-[1.65] whitespace-pre-wrap bg-slate-50 rounded-[10px] p-[14px]">
            {bodyText || <span className="text-slate-400 italic">Không có nội dung</span>}
          </div>
        </div>

        {/* Footer actions — per type */}
        <div className="shrink-0 border-t border-slate-200 px-5 py-[14px] flex flex-wrap gap-2">
          {item.type === 'Email' && (
            <>
              <button className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-white border border-slate-200 text-slate-700 hover:bg-slate-50 shadow-sm transition-colors">
                <Eye className="w-4 h-4 text-slate-500" />
                <span>Đánh dấu đã đọc</span>
              </button>
              <button
                onClick={() => onToggleImportant?.(item.id, !item.isImportant)}
                className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-white border border-slate-200 text-slate-700 hover:bg-slate-50 shadow-sm transition-colors"
              >
                <Star className={`w-4 h-4 ${item.isImportant ? 'fill-amber-400 text-amber-400' : 'text-slate-400'}`} />
                <span>{item.isImportant ? 'Bỏ quan trọng' : 'Đánh dấu quan trọng'}</span>
              </button>
              <button className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-medium text-slate-600 hover:bg-slate-100 transition-colors">
                <Tag className="w-4 h-4 text-slate-400" /><span>Nhãn</span>
              </button>
              <button className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-medium text-slate-600 hover:bg-slate-100 transition-colors">
                <Send className="w-4 h-4 text-slate-400" /><span>Soạn mới</span>
              </button>
              <button className="w-[36px] h-[36px] inline-flex items-center justify-center rounded-lg bg-rose-50 border border-rose-200 text-rose-600 hover:bg-rose-100 transition-colors ml-auto">
                <Trash className="w-4 h-4" />
              </button>
            </>
          )}

          {item.type === 'Event' && (
            <>
              <button className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-indigo-600 text-white hover:bg-indigo-700 shadow-sm transition-colors">
                <Save className="w-4 h-4" /><span>Lưu thay đổi</span>
              </button>
              <button className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-white border border-rose-200 text-rose-600 hover:bg-rose-50 shadow-sm transition-colors ml-auto">
                <Trash className="w-4 h-4" /><span>Xoá</span>
              </button>
            </>
          )}

          {item.type === 'File' && (
            <>
              <button className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-indigo-600 text-white hover:bg-indigo-700 shadow-sm transition-colors">
                <Save className="w-4 h-4" /><span>Đổi tên</span>
              </button>
              <button className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-white border border-slate-200 text-slate-700 hover:bg-slate-50 shadow-sm transition-colors">
                <ExternalLink className="w-4 h-4 text-slate-500" /><span>Mở trên Drive</span>
              </button>
              <button className="w-[36px] h-[36px] inline-flex items-center justify-center rounded-lg bg-rose-50 border border-rose-200 text-rose-600 hover:bg-rose-100 transition-colors ml-auto">
                <Trash className="w-4 h-4" />
              </button>
            </>
          )}

          {item.type === 'Note' && (
            <>
              <button className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-indigo-600 text-white hover:bg-indigo-700 shadow-sm transition-colors">
                <Save className="w-4 h-4" /><span>Lưu ghi chú</span>
              </button>
              <button className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-white border border-rose-200 text-rose-600 hover:bg-rose-50 shadow-sm transition-colors ml-auto">
                <Trash className="w-4 h-4" /><span>Xoá</span>
              </button>
            </>
          )}

          {item.type === 'Ticket' && (
            <>
              <button className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-semibold bg-indigo-600 text-white hover:bg-indigo-700 shadow-sm transition-colors">
                <Save className="w-4 h-4" /><span>Cập nhật Jira</span>
              </button>
              <button className="h-[36px] px-3 inline-flex items-center gap-1.5 rounded-lg text-[13px] font-medium text-slate-600 hover:bg-slate-100 transition-colors">
                <Reply className="w-4 h-4 text-slate-400" /><span>Bình luận</span>
              </button>
              <button className="w-[36px] h-[36px] inline-flex items-center justify-center rounded-lg bg-rose-50 border border-rose-200 text-rose-600 hover:bg-rose-100 transition-colors ml-auto">
                <Trash className="w-4 h-4" />
              </button>
            </>
          )}
        </div>

        {/* Mock write-back footer */}
        <div className="shrink-0 px-5 pb-4 flex items-center gap-2 flex-wrap">
          <span className="text-[12px] text-slate-400">Mô phỏng write-back:</span>
          <button className="px-2.5 py-1 text-[11.5px] font-medium text-slate-600 bg-white border border-slate-200 rounded-[6px] hover:border-slate-400 transition-colors">
            409 xung đột
          </button>
          <button className="px-2.5 py-1 text-[11.5px] font-medium text-slate-600 bg-white border border-slate-200 rounded-[6px] hover:border-slate-400 transition-colors">
            403 thiếu scope
          </button>
        </div>
      </div>

      {/* slide-in animation */}
      <style>{`
        @keyframes wh-slide-in { from { transform: translateX(100%); } to { transform: translateX(0); } }
      `}</style>
    </div>
  );
}
