import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { scheduledEmailsApi, type CreateScheduledEmailRequest, type ScheduledEmailDto } from '../lib/scheduledEmailsApi';
import { connectionsApi } from '../lib/connectionsApi';
import { Send, Clock, ChevronLeft, ChevronRight, AlertCircle, X, Mail, Users, Calendar } from 'lucide-react';
import toast from 'react-hot-toast';
import { AxiosError } from 'axios';
import DatePicker, { registerLocale } from 'react-datepicker';
import 'react-datepicker/dist/react-datepicker.css';
import { vi } from 'date-fns/locale/vi';
import DOMPurify from 'dompurify';

registerLocale('vi', vi);

// ─── Helpers ─────────────────────────────────────────────────────────────────
function getStatusConfig(status?: string) {
  if (!status) return { label: 'Không xác định', fg: 'text-gray-500', bg: 'bg-gray-100', dot: 'bg-gray-400' };
  switch (status.toLowerCase()) {
    case 'pending':
      return { label: 'Chờ gửi', fg: 'text-amber-600', bg: 'bg-amber-100', dot: 'bg-amber-600' };
    case 'sent':
      return { label: 'Đã gửi', fg: 'text-emerald-700', bg: 'bg-emerald-100', dot: 'bg-emerald-600' };
    case 'failed':
      return { label: 'Thất bại', fg: 'text-red-700', bg: 'bg-red-100', dot: 'bg-red-600' };
    case 'cancelled':
      return { label: 'Đã huỷ', fg: 'text-gray-500', bg: 'bg-gray-100', dot: 'bg-gray-400' };
    default:
      return { label: status, fg: 'text-gray-600', bg: 'bg-gray-100', dot: 'bg-gray-500' };
  }
}

// ─── Detail Modal ─────────────────────────────────────────────────────────────
interface DetailModalProps {
  email: ScheduledEmailDto;
  connectionName: string;
  onClose: () => void;
  onCancel: (id: string) => void;
  isCancelling: boolean;
}

const DetailModal = ({ email, connectionName, onClose, onCancel, isCancelling }: DetailModalProps) => {
  const conf = getStatusConfig(email.status);
  const sendAtLocal = new Date(email.sendAt).toLocaleString('vi-VN', {
    day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit', hour12: false
  });
  const canCancel = (email.status ?? '').toLowerCase() === 'pending';

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/40 backdrop-blur-sm"
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-3xl overflow-hidden flex flex-col max-h-[90vh]">
        {/* Header */}
        <div className="flex items-start justify-between gap-3 px-6 pt-5 pb-4 border-b border-gray-100">
          <div className="flex-1 min-w-0">
            <h2 className="text-base font-semibold text-gray-900 leading-tight mb-2 pr-6" title={email.subject}>
              {email.subject || '(Không tiêu đề)'}
            </h2>
            <span className={`inline-flex items-center space-x-1.5 px-2.5 py-1 rounded-full text-xs font-medium ${conf.bg} ${conf.fg}`}>
              <span className={`w-1.5 h-1.5 rounded-full ${conf.dot}`}></span>
              <span>{conf.label}</span>
            </span>
          </div>
          <button
            onClick={onClose}
            className="shrink-0 w-7 h-7 flex items-center justify-center rounded-full text-gray-400 hover:bg-gray-100 hover:text-gray-700 transition-colors"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Meta */}
        <div className="px-6 py-4 space-y-3 border-b border-gray-100">
          <div className="flex items-start gap-2.5">
            <Users className="w-4 h-4 text-gray-400 mt-0.5 shrink-0" />
            <div>
              <p className="text-xs text-gray-400 mb-0.5">Đến</p>
              <p className="text-sm text-gray-800">{email.to.join(', ')}</p>
            </div>
          </div>
          {email.cc.length > 0 && (
            <div className="flex items-start gap-2.5">
              <Users className="w-4 h-4 text-gray-400 mt-0.5 shrink-0" />
              <div>
                <p className="text-xs text-gray-400 mb-0.5">Cc</p>
                <p className="text-sm text-gray-800">{email.cc.join(', ')}</p>
              </div>
            </div>
          )}
          {email.bcc.length > 0 && (
            <div className="flex items-start gap-2.5">
              <Users className="w-4 h-4 text-gray-400 mt-0.5 shrink-0" />
              <div>
                <p className="text-xs text-gray-400 mb-0.5">Bcc</p>
                <p className="text-sm text-gray-800">{email.bcc.join(', ')}</p>
              </div>
            </div>
          )}
          <div className="flex items-start gap-2.5">
            <Calendar className="w-4 h-4 text-gray-400 mt-0.5 shrink-0" />
            <div>
              <p className="text-xs text-gray-400 mb-0.5">Thời gian gửi</p>
              <p className="text-sm text-gray-800">{sendAtLocal}</p>
            </div>
          </div>
          <div className="flex items-start gap-2.5">
            <Mail className="w-4 h-4 text-gray-400 mt-0.5 shrink-0" />
            <div>
              <p className="text-xs text-gray-400 mb-0.5">Kết nối</p>
              <p className="text-sm text-gray-800">Gmail · {connectionName}</p>
            </div>
          </div>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-6 py-4">
          <p className="text-xs text-gray-400 mb-2">Nội dung</p>
          {email.bodyHtml ? (() => {
            const isHtml = /<[a-z][\s\S]*>/i.test(email.bodyHtml);
            return (
              <div
                className={`bg-gray-50 rounded-xl p-4 border border-gray-100 html-content overflow-hidden ${!isHtml ? 'whitespace-pre-wrap' : ''}`}
                dangerouslySetInnerHTML={{ __html: DOMPurify.sanitize(email.bodyHtml) }}
              />
            );
          })() : (
            <p className="text-sm text-gray-400 italic">(Không có nội dung)</p>
          )}
        </div>

        {/* Error */}
        {email.lastError && (
          <div className="mx-6 mb-3 text-xs text-red-600 bg-red-50 border border-red-100 p-3 rounded-xl">
            <span className="font-medium">Lỗi:</span> {email.lastError}
          </div>
        )}

        {/* Footer */}
        <div className="px-6 pb-5 pt-3 flex justify-end gap-3 border-t border-gray-100">
          <button
            onClick={onClose}
            className="px-4 py-2 text-sm font-medium text-gray-600 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
          >
            Đóng
          </button>
          {canCancel && (
            <button
              onClick={() => { onCancel(email.id); }}
              disabled={isCancelling}
              className="px-4 py-2 text-sm font-medium text-white bg-red-500 hover:bg-red-600 rounded-lg transition-colors disabled:opacity-50"
            >
              {isCancelling ? 'Đang huỷ...' : 'Huỷ lịch gửi'}
            </button>
          )}
        </div>
      </div>
    </div>
  );
};

// ─── Main Page ────────────────────────────────────────────────────────────────
export const ScheduledEmails = () => {
  const queryClient = useQueryClient();
  const limit = 10;
  const [page, setPage] = useState(1);
  const [statusFilter, setStatusFilter] = useState('All');

  // Form State
  const [cTo, setCTo] = useState('');
  const [cCc, setCCc] = useState('');
  const [cBcc, setCBcc] = useState('');
  const [cSubject, setCSubject] = useState('');
  const [cBody, setCBody] = useState('');
  const [cWhen, setCWhen] = useState<Date | null>(null);
  const [cConn, setCConn] = useState('');

  // Detail modal
  const [selectedEmail, setSelectedEmail] = useState<ScheduledEmailDto | null>(null);

  // Fetch connections for the dropdown
  const { data: connections = [] } = useQuery({
    queryKey: ['connections'],
    queryFn: connectionsApi.getConnections,
  });

  const activeGmailConnections = React.useMemo(() => connections.filter(
    c => c.serviceType.toLowerCase() === 'gmail' && c.status.toLowerCase() === 'active'
  ), [connections]);

  // Fetch scheduled emails
  const skip = (page - 1) * limit;
  const { data: schedData, isLoading, isError, refetch } = useQuery({
    queryKey: ['scheduled-emails', page, limit, statusFilter],
    queryFn: () => scheduledEmailsApi.getScheduledEmails(skip, limit, statusFilter),
  });

  const createMutation = useMutation({
    mutationFn: scheduledEmailsApi.createScheduledEmail,
    onSuccess: () => {
      toast.success('Đã lên lịch gửi email thành công!');
      setCTo('');
      setCCc('');
      setCBcc('');
      setCSubject('');
      setCBody('');
      setCWhen(null);
      queryClient.invalidateQueries({ queryKey: ['scheduled-emails'] });
      setPage(1);
    },
    onError: (err: AxiosError<{ message?: string; details?: string[] }>) => {
      const details = err.response?.data?.details;
      if (details && details.length > 0) {
        details.forEach(d => toast.error(d));
      } else {
        toast.error(err.response?.data?.message || 'Không thể lên lịch gửi email');
      }
    }
  });

  const cancelMutation = useMutation({
    mutationFn: scheduledEmailsApi.cancelScheduledEmail,
    onSuccess: () => {
      toast.success('Đã huỷ lịch gửi!');
      queryClient.invalidateQueries({ queryKey: ['scheduled-emails'] });
    },
    onError: (err: AxiosError<{ message?: string }>) => {
      const msg = err.response?.data?.message || 'Không thể huỷ lịch gửi';
      toast.error(msg);
    }
  });

  const handleScheduleSend = () => {
    if (!cTo.trim()) return toast.error('Vui lòng nhập người nhận');
    if (!cSubject.trim()) return toast.error('Vui lòng nhập tiêu đề email');
    if (!cWhen) return toast.error('Vui lòng chọn thời gian gửi');
    if (!cConn) return toast.error('Vui lòng chọn kết nối Gmail');
    if (cWhen <= new Date()) return toast.error('Thời gian gửi phải sau thời điểm hiện tại');

    const splitAndTrim = (str: string) => str.split(',').map(s => s.trim()).filter(Boolean);
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

    const toList = splitAndTrim(cTo);
    const ccList = splitAndTrim(cCc);
    const bccList = splitAndTrim(cBcc);

    const invalidTo = toList.find(e => !emailRegex.test(e));
    if (invalidTo) return toast.error(`"${invalidTo}" không phải email hợp lệ (To)`);

    const invalidCc = ccList.find(e => !emailRegex.test(e));
    if (invalidCc) return toast.error(`"${invalidCc}" không phải email hợp lệ (Cc)`);

    const invalidBcc = bccList.find(e => !emailRegex.test(e));
    if (invalidBcc) return toast.error(`"${invalidBcc}" không phải email hợp lệ (Bcc)`);

    const payload: CreateScheduledEmailRequest = {
      connectionId: cConn,
      to: toList,
      cc: ccList,
      bcc: bccList,
      subject: cSubject,
      bodyHtml: cBody,
      sendAt: cWhen.toISOString(),
    };

    createMutation.mutate(payload);
  };

  const scheduledList = schedData?.value || [];
  const totalItems = schedData?.['@odata.count'] || 0;
  const totalPages = Math.max(1, Math.ceil(totalItems / limit));
  const hasItems = scheduledList.length > 0;

  React.useEffect(() => {
    if (!cConn && activeGmailConnections.length > 0) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setCConn(activeGmailConnections[0].id);
    }
  }, [cConn, activeGmailConnections]);

  const renderPagination = () => {
    if (totalItems === 0) return null;
    const maxButtons = 5;
    let startPage = Math.max(1, page - Math.floor(maxButtons / 2));
    let endPage = startPage + maxButtons - 1;
    if (endPage > totalPages) {
      endPage = totalPages;
      startPage = Math.max(1, endPage - maxButtons + 1);
    }
    const pageButtons = Array.from({ length: endPage - startPage + 1 }, (_, i) => startPage + i);
    const startItem = skip + 1;
    const endItem = Math.min(skip + limit, totalItems);
    return (
      <div className="flex items-center justify-between mt-4 flex-wrap gap-2">
        <span className="text-sm text-gray-500">{startItem}–{endItem} trên {totalItems} mục</span>
        <div className="flex items-center space-x-1">
          <button onClick={() => setPage(Math.max(1, page - 1))} disabled={page === 1} className="w-7 h-7 flex items-center justify-center rounded-md text-gray-500 hover:bg-gray-200 transition-colors disabled:opacity-50 disabled:cursor-not-allowed">
            <ChevronLeft className="w-4 h-4" />
          </button>
          {pageButtons.map((p) => (
            <button key={p} onClick={() => setPage(p)} className={`w-7 h-7 flex items-center justify-center rounded-md text-sm font-medium transition-colors ${p === page ? 'bg-gray-200 text-gray-900' : 'text-gray-500 hover:bg-gray-100'}`}>{p}</button>
          ))}
          <button onClick={() => setPage(Math.min(totalPages, page + 1))} disabled={page === totalPages} className="w-7 h-7 flex items-center justify-center rounded-md text-gray-500 hover:bg-gray-200 transition-colors disabled:opacity-50 disabled:cursor-not-allowed">
            <ChevronRight className="w-4 h-4" />
          </button>
        </div>
      </div>
    );
  };

  const inputClass = "w-full h-9 px-3 border border-gray-300 rounded-lg text-sm mb-3 focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500 transition-colors";
  const labelClass = "block text-xs font-medium text-gray-500 mb-1.5";

  return (
    <>
      {selectedEmail && (
        <DetailModal
          email={selectedEmail}
          connectionName={connections.find(c => c.id === selectedEmail.connectionId)?.providerAccountId || 'Unknown'}
          onClose={() => setSelectedEmail(null)}
          onCancel={(id) => cancelMutation.mutate(id, { onSuccess: () => setSelectedEmail(null) })}
          isCancelling={cancelMutation.isPending}
        />
      )}

      <div className="p-5 md:p-8 max-w-6xl mx-auto h-[calc(100vh-64px)] flex flex-col overflow-hidden">
        <div className="mb-6 shrink-0">
          <h1 className="text-2xl font-bold text-gray-900 mb-1">Email hẹn giờ</h1>
          <p className="text-sm text-gray-500">Soạn và lên lịch gửi email tự động qua Gmail đã kết nối.</p>
        </div>

        <div className="flex flex-col lg:flex-row gap-6 items-stretch flex-1 min-h-0">
          {/* Compose Form */}
          <div className="flex-1 w-full lg:w-1/2 bg-white border border-gray-200 rounded-xl p-5 md:p-6 shadow-sm flex flex-col min-h-0">
            <h2 className="text-base font-semibold text-gray-900 mb-4 shrink-0">Soạn email</h2>

            <label className={labelClass}>Người nhận</label>
            <input value={cTo} onChange={(e) => setCTo(e.target.value)} placeholder="email1@..., email2@..." className={inputClass} />

            <div className="flex flex-col sm:flex-row sm:gap-3">
              <div className="flex-1">
                <label className={labelClass}>Cc</label>
                <input value={cCc} onChange={(e) => setCCc(e.target.value)} placeholder="email1@..., email2@..." className={inputClass} />
              </div>
              <div className="flex-1">
                <label className={labelClass}>Bcc</label>
                <input value={cBcc} onChange={(e) => setCBcc(e.target.value)} placeholder="email1@..., email2@..." className={inputClass} />
              </div>
            </div>

            <label className={labelClass}>Tiêu đề</label>
            <input value={cSubject} onChange={(e) => setCSubject(e.target.value)} placeholder="Tiêu đề email" className={inputClass} />

            <label className={`${labelClass} shrink-0`}>Nội dung</label>
            <textarea
              value={cBody}
              onChange={(e) => setCBody(e.target.value)}
              placeholder="Soạn nội dung email..."
              className="w-full px-3 py-2.5 border border-gray-300 rounded-lg text-sm mb-3 focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500 transition-colors resize-none leading-relaxed flex-1 min-h-[100px]"
            />

            <div className="flex flex-col sm:flex-row sm:gap-3 shrink-0">
              <div className="flex-1">
                <label className={`${labelClass} shrink-0`}>Thời gian gửi</label>
                <DatePicker
                  selected={cWhen}
                  onChange={(date: Date | null) => setCWhen(date)}
                  showTimeSelect
                  timeFormat="HH:mm"
                  timeIntervals={15}
                  timeCaption="Giờ"
                  dateFormat="dd/MM/yyyy HH:mm"
                  locale="vi"
                  placeholderText="dd/MM/yyyy HH:mm"
                  className={inputClass}
                />
              </div>
              <div className="flex-1">
                <label className={labelClass}>Kết nối</label>
                <select value={cConn} onChange={(e) => setCConn(e.target.value)} className={inputClass}>
                  <option value="">Chọn kết nối...</option>
                  {activeGmailConnections.map(c => (
                    <option key={c.id} value={c.id}>Gmail · {c.providerAccountId}</option>
                  ))}
                </select>
              </div>
            </div>

            <button
              onClick={handleScheduleSend}
              disabled={createMutation.isPending}
              className="mt-2 w-full shrink-0 flex items-center justify-center space-x-2 py-2.5 rounded-lg text-sm font-semibold bg-brand-600 hover:bg-brand-700 text-white transition-colors disabled:opacity-70 disabled:cursor-not-allowed"
            >
              <Send className="w-4 h-4" />
              <span>{createMutation.isPending ? 'Đang xử lý...' : 'Lên lịch gửi'}</span>
            </button>
          </div>

          {/* Scheduled List */}
          <div className="flex-1 w-full lg:w-1/2 flex flex-col min-h-0">
            <div className="flex items-center justify-between mb-3.5 shrink-0">
              <h2 className="text-base font-semibold text-gray-900">Lịch đã đặt</h2>
              <select
                value={statusFilter}
                onChange={(e) => {
                  setStatusFilter(e.target.value);
                  setPage(1);
                }}
                className="h-8 px-2 border border-gray-300 rounded-lg text-sm bg-white focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500 transition-colors"
              >
                <option value="All">Tất cả</option>
                <option value="Pending">Chờ gửi</option>
                <option value="Sent">Đã gửi</option>
                <option value="Failed">Thất bại</option>
                <option value="Cancelled">Đã huỷ</option>
              </select>
            </div>

            {isError ? (
              <div className="bg-white border border-red-200 rounded-xl p-8 text-center shadow-sm">
                <div className="w-12 h-12 rounded-xl bg-red-50 text-red-500 flex items-center justify-center mx-auto mb-3">
                  <AlertCircle className="w-6 h-6" />
                </div>
                <h3 className="text-sm font-semibold text-gray-900 mb-1">Không tải được lịch</h3>
                <p className="text-xs text-gray-500 mb-4">Vui lòng thử lại sau.</p>
                <button onClick={() => refetch()} className="px-4 py-2 text-sm font-medium bg-brand-600 text-white rounded-lg hover:bg-brand-700">Thử lại</button>
              </div>
            ) : isLoading ? (
              <div className="space-y-3">
                {[1, 2, 3].map(i => (
                  <div key={i} className="bg-white border border-gray-200 rounded-xl p-4 shadow-sm animate-pulse">
                    <div className="h-4 bg-gray-200 rounded w-3/4 mb-3"></div>
                    <div className="h-3 bg-gray-200 rounded w-1/2 mb-2"></div>
                    <div className="h-3 bg-gray-200 rounded w-1/3"></div>
                  </div>
                ))}
              </div>
            ) : !hasItems ? (
              <div className="bg-white border border-dashed border-gray-300 rounded-xl p-10 text-center">
                <div className="w-12 h-12 rounded-xl bg-gray-50 text-gray-400 flex items-center justify-center mx-auto mb-3">
                  <Clock className="w-6 h-6" />
                </div>
                <h3 className="text-sm font-semibold text-gray-900 mb-1">Chưa có email nào được hẹn giờ</h3>
                <p className="text-xs text-gray-500">Soạn email bên trái và đặt thời gian để lên lịch gửi.</p>
              </div>
            ) : (
              <>
                <div className="space-y-3 overflow-y-auto pr-2 flex-1 min-h-0">
                  {scheduledList.map(s => {
                    const conf = getStatusConfig(s.status);
                    const connectionName = connections.find(c => c.id === s.connectionId)?.providerAccountId || 'Unknown Connection';
                    const canCancel = (s.status ?? '').toLowerCase() === 'pending';
                    const sendAtLocal = new Date(s.sendAt).toLocaleString('vi-VN', {
                      day: '2-digit', month: '2-digit', year: 'numeric',
                      hour: '2-digit', minute: '2-digit', hour12: false
                    });

                    return (
                      <div
                        key={s.id}
                        onClick={() => setSelectedEmail(s)}
                        className="bg-white border border-gray-200 rounded-xl p-4 shadow-sm hover:shadow-md hover:border-brand-300 transition-all cursor-pointer group"
                      >
                        <div className="flex items-start justify-between gap-3 mb-2">
                          <h4 className="text-sm font-medium text-gray-900 truncate group-hover:text-brand-600 transition-colors" title={s.subject}>
                            {s.subject || '(Không tiêu đề)'}
                          </h4>
                          <span className={`inline-flex items-center space-x-1.5 px-2.5 py-1 rounded-full text-xs font-medium shrink-0 ${conf.bg} ${conf.fg}`}>
                            <span className={`w-1.5 h-1.5 rounded-full ${conf.dot}`}></span>
                            <span>{conf.label}</span>
                          </span>
                        </div>
                        <div className="text-[13px] text-gray-500 mb-2 truncate" title={s.to.join(', ')}>
                          Đến: {s.to.join(', ')}
                        </div>
                        <div className="flex items-center gap-1.5 text-xs text-gray-400">
                          <Clock className="w-3.5 h-3.5" />
                          <span className="truncate">{sendAtLocal} · Gmail ({connectionName})</span>
                        </div>
                        {s.lastError && (
                          <div className="mt-2 text-xs text-red-600 bg-red-50 p-2 rounded-md">{s.lastError}</div>
                        )}
                        {canCancel && (
                          <div className="mt-3 flex justify-end">
                            <button
                              onClick={(e) => { e.stopPropagation(); cancelMutation.mutate(s.id); }}
                              disabled={cancelMutation.isPending}
                              className="px-3 py-1.5 text-xs font-medium text-red-600 bg-white border border-gray-200 rounded-md hover:border-red-200 hover:bg-red-50 transition-colors disabled:opacity-50"
                            >
                              {cancelMutation.isPending && cancelMutation.variables === s.id ? 'Đang huỷ...' : 'Huỷ lịch'}
                            </button>
                          </div>
                        )}
                      </div>
                    );
                  })}
                </div>
                <div className="shrink-0 mt-2">
                  {renderPagination()}
                </div>
              </>
            )}
          </div>
        </div>
      </div>
    </>
  );
};
