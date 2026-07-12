import React, { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { scheduledEmailsApi, type CreateScheduledEmailRequest, type ScheduledEmailDto } from '../lib/scheduledEmailsApi';
import type { ODataResponse } from '../lib/odata';
import { connectionsApi } from '../lib/connectionsApi';
import { Send, Clock, ChevronLeft, ChevronRight, AlertCircle, X, Mail, Users, Calendar, Pencil } from 'lucide-react';
import toast from 'react-hot-toast';
import { handleApiError } from '../lib/errorUtils';
import DOMPurify from 'dompurify';
import { DateTimePicker } from '../components/DateTimePicker';
import { Select } from '../components/Select';
import { EmailChipsInput } from '../components/EmailChipsInput';
import { RichTextEditor } from '../components/RichTextEditor';
import { EMAIL_TEMPLATES } from '../lib/emailTemplates';
import { sendEmailApi, fileToAttachmentUpload, MAX_ATTACHMENT_TOTAL_BYTES } from '../lib/sendEmailApi';
import { PageSizeSelect } from '../components/PageSizeSelect';
import { useI18n } from '../hooks/useI18n';
import type { TranslationKey } from '../i18n/translations';

// ─── Helpers ─────────────────────────────────────────────────────────────────
type T = (k: TranslationKey) => string;
function getStatusConfig(status: string | undefined, t: T) {
  if (!status) return { label: t('schedEmail.statusUnknown'), fg: 'text-gray-500 dark:text-slate-400', bg: 'bg-gray-100 dark:bg-slate-800', dot: 'bg-gray-400' };
  switch (status.toLowerCase()) {
    case 'pending':
      return { label: t('schedEmail.statusPending'), fg: 'text-amber-600 dark:text-amber-400', bg: 'bg-amber-100 dark:bg-amber-500/10', dot: 'bg-amber-600' };
    case 'sent':
      return { label: t('schedEmail.statusSent'), fg: 'text-emerald-700 dark:text-emerald-400', bg: 'bg-emerald-100 dark:bg-emerald-500/10', dot: 'bg-emerald-600' };
    case 'failed':
      return { label: t('schedEmail.statusFailed'), fg: 'text-red-700 dark:text-red-400', bg: 'bg-red-100 dark:bg-red-500/10', dot: 'bg-red-600' };
    case 'cancelled':
      return { label: t('schedEmail.statusCancelled'), fg: 'text-gray-500 dark:text-slate-400', bg: 'bg-gray-100 dark:bg-slate-800', dot: 'bg-gray-400' };
    default:
      return { label: status, fg: 'text-gray-600 dark:text-slate-400', bg: 'bg-gray-100 dark:bg-slate-800', dot: 'bg-gray-500' };
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
  const { t, lang } = useI18n();
  const conf = getStatusConfig(email.status, t);
  const sendAtLocal = new Date(email.sendAt).toLocaleString(lang === 'vi' ? 'vi-VN' : 'en-US', {
    day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit', hour12: false
  });
  const canCancel = (email.status ?? '').toLowerCase() === 'pending';

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/40 backdrop-blur-sm"
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div className="bg-white dark:bg-slate-900 rounded-2xl shadow-2xl w-full max-w-3xl overflow-hidden flex flex-col max-h-[90vh]">
        {/* Header */}
        <div className="flex items-start justify-between gap-3 px-6 pt-5 pb-4 border-b border-gray-100 dark:border-slate-800">
          <div className="flex-1 min-w-0">
            <h2 className="text-base font-semibold text-gray-900 dark:text-slate-100 leading-tight mb-2 pr-6" title={email.subject}>
              {email.subject || t('sendEmail.noSubject')}
            </h2>
            <span className={`inline-flex items-center space-x-1.5 px-2.5 py-1 rounded-full text-xs font-medium ${conf.bg} ${conf.fg}`}>
              <span className={`w-1.5 h-1.5 rounded-full ${conf.dot}`}></span>
              <span>{conf.label}</span>
            </span>
          </div>
          <button
            onClick={onClose}
            className="shrink-0 w-7 h-7 flex items-center justify-center rounded-full text-gray-400 dark:text-slate-500 hover:bg-gray-100 dark:hover:bg-slate-800 hover:text-gray-700 dark:hover:text-slate-200 transition-colors"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Meta */}
        <div className="px-6 py-4 space-y-3 border-b border-gray-100 dark:border-slate-800">
          <div className="flex items-start gap-2.5">
            <Users className="w-4 h-4 text-gray-400 dark:text-slate-500 mt-0.5 shrink-0" />
            <div>
              <p className="text-xs text-gray-400 dark:text-slate-500 mb-0.5">{t('schedEmail.detailTo')}</p>
              <p className="text-sm text-gray-800 dark:text-slate-200">{email.to.join(', ')}</p>
            </div>
          </div>
          {email.cc.length > 0 && (
            <div className="flex items-start gap-2.5">
              <Users className="w-4 h-4 text-gray-400 dark:text-slate-500 mt-0.5 shrink-0" />
              <div>
                <p className="text-xs text-gray-400 dark:text-slate-500 mb-0.5">Cc</p>
                <p className="text-sm text-gray-800 dark:text-slate-200">{email.cc.join(', ')}</p>
              </div>
            </div>
          )}
          {email.bcc.length > 0 && (
            <div className="flex items-start gap-2.5">
              <Users className="w-4 h-4 text-gray-400 dark:text-slate-500 mt-0.5 shrink-0" />
              <div>
                <p className="text-xs text-gray-400 dark:text-slate-500 mb-0.5">Bcc</p>
                <p className="text-sm text-gray-800 dark:text-slate-200">{email.bcc.join(', ')}</p>
              </div>
            </div>
          )}
          <div className="flex items-start gap-2.5">
            <Calendar className="w-4 h-4 text-gray-400 dark:text-slate-500 mt-0.5 shrink-0" />
            <div>
              <p className="text-xs text-gray-400 dark:text-slate-500 mb-0.5">{t('schedEmail.sendTime')}</p>
              <p className="text-sm text-gray-800 dark:text-slate-200">{sendAtLocal}</p>
            </div>
          </div>
          <div className="flex items-start gap-2.5">
            <Mail className="w-4 h-4 text-gray-400 dark:text-slate-500 mt-0.5 shrink-0" />
            <div>
              <p className="text-xs text-gray-400 dark:text-slate-500 mb-0.5">{t('sendEmail.connection')}</p>
              <p className="text-sm text-gray-800 dark:text-slate-200">Gmail · {connectionName}</p>
            </div>
          </div>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-6 py-4">
          <p className="text-xs text-gray-400 dark:text-slate-500 mb-2">{t('sendEmail.content')}</p>
          {email.bodyHtml ? (() => {
            const isHtml = /<[a-z][\s\S]*>/i.test(email.bodyHtml);
            return (
              <div
                className={`bg-gray-50 dark:bg-slate-800 rounded-xl p-4 border border-gray-100 dark:border-slate-800 html-content overflow-hidden ${!isHtml ? 'whitespace-pre-wrap' : ''}`}
                dangerouslySetInnerHTML={{ __html: DOMPurify.sanitize(email.bodyHtml) }}
              />
            );
          })() : (
            <p className="text-sm text-gray-400 dark:text-slate-500 italic">{t('schedEmail.noContent')}</p>
          )}
        </div>

        {/* Error */}
        {email.lastError && (
          <div className="mx-6 mb-3 text-xs text-red-600 dark:text-red-400 bg-red-50 dark:bg-red-500/10 border border-red-100 dark:border-red-500/20 p-3 rounded-xl">
            <span className="font-medium">{t('schedEmail.errorLabel')}</span> {email.lastError}
          </div>
        )}

        {/* Footer */}
        <div className="px-6 pb-5 pt-3 flex justify-end gap-3 border-t border-gray-100 dark:border-slate-800">
          <button
            onClick={onClose}
            className="px-4 py-2 text-sm font-medium text-gray-600 dark:text-slate-200 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition-colors"
          >
            {t('common.close')}
          </button>
          {canCancel && (
            <button
              onClick={() => { onCancel(email.id); }}
              disabled={isCancelling}
              className="px-4 py-2 text-sm font-medium text-white bg-red-500 hover:bg-red-600 rounded-lg transition-colors disabled:opacity-50"
            >
              {isCancelling ? t('schedEmail.cancelling') : t('schedEmail.cancelSchedule')}
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
  const { t, lang } = useI18n();
  const dateLocale = lang === 'vi' ? 'vi-VN' : 'en-US';
  const [limit, setLimit] = useState(10);
  const [page, setPage] = useState(1);
  const [statusFilter, setStatusFilter] = useState('All');

  // Form State
  const [cTo, setCTo] = useState<string[]>([]);
  const [cCc, setCCc] = useState<string[]>([]);
  const [cBcc, setCBcc] = useState<string[]>([]);
  const [cSubject, setCSubject] = useState('');
  const [cBody, setCBody] = useState('');
  const [cFiles, setCFiles] = useState<File[]>([]);
  const [cWhen, setCWhen] = useState<Date | null>(null);
  const [cConn, setCConn] = useState('');
  const [template, setTemplate] = useState('blank');
  const [includeSignature, setIncludeSignature] = useState(true);

  // Detail modal
  const [selectedEmail, setSelectedEmail] = useState<ScheduledEmailDto | null>(null);

  // Mở popup chi tiết trực tiếp qua ?open={id} (deep-link từ Calendar).
  const [searchParams, setSearchParams] = useSearchParams();
  const openId = searchParams.get('open');
  const { data: openedEmail } = useQuery({
    queryKey: ['scheduled-email', openId],
    queryFn: () => scheduledEmailsApi.getScheduledEmailById(openId!),
    enabled: !!openId,
  });
  useEffect(() => {
    if (!openedEmail) return;
    setSelectedEmail(openedEmail);
    searchParams.delete('open');
    setSearchParams(searchParams, { replace: true });
  }, [openedEmail]); // eslint-disable-line react-hooks/exhaustive-deps

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
  const { data: schedData, isLoading, isError, refetch } = useQuery<ODataResponse<ScheduledEmailDto>>({
    queryKey: ['scheduled-emails', page, limit, statusFilter],
    queryFn: () => scheduledEmailsApi.getScheduledEmails(skip, limit, statusFilter),
    // Auto-cron đổi status phía server → poll để UI đồng bộ. Chỉ poll khi còn item "Chờ gửi"
    // trong danh sách hiện tại; hết Pending thì ngừng (tránh request thừa).
    refetchInterval: (query) => {
      const items = query.state.data?.value ?? [];
      const hasPending = items.some(e => (e.status ?? '').toLowerCase() === 'pending');
      return hasPending ? 15000 : false;
    },
    refetchOnWindowFocus: true,
  });

  const createMutation = useMutation({
    mutationFn: scheduledEmailsApi.createScheduledEmail,
    onSuccess: () => {
      toast.success(t('schedEmail.created'));
      setCTo([]);
      setCCc([]);
      setCBcc([]);
      setCSubject('');
      setCBody('');
      setCFiles([]);
      setCWhen(null);
      setTemplate('blank');
      queryClient.invalidateQueries({ queryKey: ['scheduled-emails'] });
      setPage(1);
    },
    onError: (err) => handleApiError(err, t('schedEmail.createFail'))
  });

  const cancelMutation = useMutation({
    mutationFn: scheduledEmailsApi.cancelScheduledEmail,
    onSuccess: () => {
      toast.success(t('schedEmail.cancelled'));
      queryClient.invalidateQueries({ queryKey: ['scheduled-emails'] });
    },
    onError: (err) => handleApiError(err, t('schedEmail.cancelFail'))
  });

  // cConn có thể rỗng khi user chưa chủ động chọn — mặc định về Gmail connection đầu tiên.
  const resolvedConn = cConn || activeGmailConnections[0]?.id || '';

  // Chữ ký THẬT từ Gmail của connection (rỗng nếu chưa đặt / connection cũ thiếu scope settings.basic).
  const { data: signature = '' } = useQuery({
    queryKey: ['gmail-signature', resolvedConn],
    queryFn: () => sendEmailApi.getSignature(resolvedConn),
    enabled: !!resolvedConn,
    staleTime: 5 * 60 * 1000,
  });

  const composedHtml = includeSignature && signature
    ? `${cBody}<br><br>${signature}`
    : cBody;

  const applyTemplate = (id: string) => {
    setTemplate(id);
    const tpl = EMAIL_TEMPLATES.find(t => t.id === id);
    if (!tpl) return;
    setCBody(tpl.html);
    if (tpl.subject && !cSubject.trim()) setCSubject(tpl.subject);
  };

  const handleScheduleSend = async () => {
    // To/Cc/Bcc đã được EmailChipsInput validate từng email lúc thêm → chỉ cần check rỗng.
    if (cTo.length === 0) return toast.error(t('sendEmail.needTo'));
    if (!cSubject.trim()) return toast.error(t('sendEmail.needSubject'));
    if (!cWhen) return toast.error(t('schedEmail.needTime'));
    if (!resolvedConn) return toast.error(t('sendEmail.needConn'));
    if (cWhen <= new Date()) return toast.error(t('schedEmail.needFuture'));

    const totalSize = cFiles.reduce((sum, f) => sum + f.size, 0);
    if (totalSize > MAX_ATTACHMENT_TOTAL_BYTES) return toast.error(t('attach.tooLarge'));
    const attachments = cFiles.length > 0 ? await Promise.all(cFiles.map(fileToAttachmentUpload)) : undefined;

    const payload: CreateScheduledEmailRequest = {
      connectionId: resolvedConn,
      to: cTo,
      cc: cCc,
      bcc: cBcc,
      subject: cSubject,
      bodyHtml: composedHtml,
      attachments,
      sendAt: cWhen.toISOString(),
    };

    createMutation.mutate(payload);
  };

  const scheduledList = schedData?.value || [];
  const totalItems = schedData?.['@odata.count'] || 0;
  const totalPages = Math.max(1, Math.ceil(totalItems / limit));
  const hasItems = scheduledList.length > 0;



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
        <div className="flex items-center gap-3">
          <PageSizeSelect value={limit} onChange={(n) => { setLimit(n); setPage(1); }} />
          <span className="text-sm text-gray-500 dark:text-slate-400">{t('inbox.range', { start: startItem, end: endItem, total: totalItems })}</span>
        </div>
        <div className="flex items-center space-x-1">
          <button onClick={() => setPage(Math.max(1, page - 1))} disabled={page === 1} className="w-7 h-7 flex items-center justify-center rounded-md text-gray-500 dark:text-slate-400 hover:bg-gray-200 dark:hover:bg-slate-800 transition-colors disabled:opacity-50 disabled:cursor-not-allowed">
            <ChevronLeft className="w-4 h-4" />
          </button>
          {pageButtons.map((p) => (
            <button key={p} onClick={() => setPage(p)} className={`w-7 h-7 flex items-center justify-center rounded-md text-sm font-medium transition-colors ${p === page ? 'bg-gray-200 dark:bg-slate-800 text-gray-900 dark:text-slate-100' : 'text-gray-500 dark:text-slate-400 hover:bg-gray-100 dark:hover:bg-slate-800'}`}>{p}</button>
          ))}
          <button onClick={() => setPage(Math.min(totalPages, page + 1))} disabled={page === totalPages} className="w-7 h-7 flex items-center justify-center rounded-md text-gray-500 dark:text-slate-400 hover:bg-gray-200 dark:hover:bg-slate-800 transition-colors disabled:opacity-50 disabled:cursor-not-allowed">
            <ChevronRight className="w-4 h-4" />
          </button>
        </div>
      </div>
    );
  };

  const inputClass = "w-full h-9 px-3 border border-gray-300 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100 dark:placeholder-slate-500 rounded-lg text-sm mb-3 focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500 transition-colors";
  const labelClass = "block text-xs font-medium text-gray-500 dark:text-slate-400 mb-1.5";

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

      <div className="p-5 md:p-8 max-w-[1600px] mx-auto h-[calc(100vh-64px)] flex flex-col overflow-hidden">
        <div className="mb-6 shrink-0">
          <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100 mb-1">{t('schedEmail.title')}</h1>
          <p className="text-sm text-gray-500 dark:text-slate-400">{t('schedEmail.subtitle')}</p>
        </div>

        <div className="flex flex-col lg:flex-row gap-6 items-stretch flex-1 min-h-0">
          {/* Compose Form */}
          <div className="flex-1 w-full lg:w-1/2 flex flex-col min-h-0">
            <div className="flex items-center gap-2 mb-3.5 shrink-0 text-gray-900 dark:text-slate-100">
              <Pencil className="w-4 h-4 text-gray-400 dark:text-slate-500" />
              <h2 className="text-base font-semibold">{t('sendEmail.compose')}</h2>
            </div>
            <div className="flex-1 min-h-0 bg-white dark:bg-slate-900 border border-gray-200 dark:border-slate-800 rounded-xl p-5 md:p-6 shadow-sm flex flex-col overflow-y-auto">
              <label className={`${labelClass} shrink-0`}>{t('sendEmail.to')}</label>
              <div className="shrink-0">
                <EmailChipsInput value={cTo} onChange={setCTo} connectionId={resolvedConn || undefined} placeholder={t('sendEmail.toPlaceholder')} />
              </div>

              <div className="flex flex-col sm:flex-row sm:gap-3 shrink-0">
                <div className="flex-1">
                  <label className={labelClass}>Cc</label>
                  <EmailChipsInput value={cCc} onChange={setCCc} connectionId={resolvedConn || undefined} placeholder="email@..." />
                </div>
                <div className="flex-1">
                  <label className={labelClass}>Bcc</label>
                  <EmailChipsInput value={cBcc} onChange={setCBcc} connectionId={resolvedConn || undefined} placeholder="email@..." />
                </div>
              </div>

              <div className="flex flex-col sm:flex-row sm:gap-3 shrink-0">
                <div className="flex-1">
                  <label className={labelClass}>{t('sendEmail.subject')}</label>
                  <input value={cSubject} onChange={(e) => setCSubject(e.target.value)} placeholder={t('sendEmail.subjectPlaceholder')} className={inputClass} />
                </div>
                <div className="sm:w-44">
                  <label className={labelClass}>{t('sendEmail.template')}</label>
                  <Select
                    value={template}
                    onChange={applyTemplate}
                    options={EMAIL_TEMPLATES.map(tpl => ({ value: tpl.id, label: t(tpl.labelKey) }))}
                    className="h-9 mb-3"
                  />
                </div>
              </div>

              <label className={`${labelClass} shrink-0`}>{t('sendEmail.content')}</label>
              <RichTextEditor
                value={cBody}
                onChange={setCBody}
                placeholder={t('sendEmail.contentPlaceholder')}
                className="mb-3 shrink-0"
                attachFiles={cFiles}
                onAttachFilesChange={setCFiles}
              />

              <div className="shrink-0 mb-4">
                <label className="flex items-center gap-2.5 cursor-pointer select-none">
                  <button
                    type="button"
                    role="switch"
                    aria-checked={includeSignature}
                    disabled={!signature}
                    onClick={() => setIncludeSignature((v) => !v)}
                    className={`relative w-9 h-5 rounded-full transition-colors shrink-0 disabled:opacity-40 ${includeSignature && signature ? 'bg-brand-600' : 'bg-gray-300 dark:bg-slate-700'}`}
                  >
                    <span className={`absolute top-0.5 left-0.5 w-4 h-4 rounded-full bg-white shadow-sm transition-transform ${includeSignature && signature ? 'translate-x-4' : ''}`} />
                  </button>
                  <span className="text-sm text-gray-700 dark:text-slate-200">{t('sendEmail.includeSignature')}</span>
                </label>
                {!signature && (
                  <p className="mt-1.5 text-xs text-gray-400 dark:text-slate-500 leading-relaxed">
                    {t('sendEmail.signatureHint')}
                  </p>
                )}
              </div>

              <div className="flex flex-col sm:flex-row sm:gap-3 shrink-0">
                <div className="flex-1">
                  <label className={`${labelClass} shrink-0`}>{t('schedEmail.sendTime')}</label>
                  <DateTimePicker
                    value={cWhen}
                    onChange={setCWhen}
                    placeholder="dd/MM/yyyy HH:mm"
                    className={inputClass}
                  />
                </div>
                <div className="flex-1">
                  <label className={labelClass}>{t('sendEmail.connection')}</label>
                  <Select
                    value={resolvedConn}
                    onChange={setCConn}
                    options={activeGmailConnections.map(c => ({ value: c.id, label: `Gmail · ${c.providerAccountId}` }))}
                    placeholder={t('sendEmail.connectionPlaceholder')}
                    className="h-9 mb-3"
                  />
                </div>
              </div>

              <button
                onClick={handleScheduleSend}
                disabled={createMutation.isPending}
                className="mt-2 w-full shrink-0 flex items-center justify-center space-x-2 py-2.5 rounded-lg text-sm font-semibold bg-brand-600 hover:bg-brand-700 text-white transition-colors disabled:opacity-70 disabled:cursor-not-allowed"
              >
                <Send className="w-4 h-4" />
                <span>{createMutation.isPending ? t('schedEmail.scheduling') : t('schedEmail.schedule')}</span>
              </button>
            </div>
          </div>

          {/* Scheduled List */}
          <div className="flex-1 w-full lg:w-1/2 flex flex-col min-h-0">
            <div className="flex items-center justify-between mb-3.5 shrink-0">
              <h2 className="text-base font-semibold text-gray-900 dark:text-slate-100">{t('schedEmail.listTitle')}</h2>
              <div className="w-36">
                <Select
                  value={statusFilter}
                  onChange={(v) => { setStatusFilter(v); setPage(1); }}
                  options={[
                    { value: 'All', label: t('schedEmail.filterAll') },
                    { value: 'Pending', label: t('schedEmail.statusPending') },
                    { value: 'Sent', label: t('schedEmail.statusSent') },
                    { value: 'Failed', label: t('schedEmail.statusFailed') },
                    { value: 'Cancelled', label: t('schedEmail.statusCancelled') },
                  ]}
                  className="h-8"
                />
              </div>
            </div>

            {isError ? (
              <div className="bg-white dark:bg-slate-900 border border-red-200 dark:border-red-500/20 rounded-xl p-8 text-center shadow-sm">
                <div className="w-12 h-12 rounded-xl bg-red-50 dark:bg-red-500/10 text-red-500 dark:text-red-400 flex items-center justify-center mx-auto mb-3">
                  <AlertCircle className="w-6 h-6" />
                </div>
                <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100 mb-1">{t('schedEmail.loadError')}</h3>
                <p className="text-xs text-gray-500 dark:text-slate-400 mb-4">{t('integrations.loadErrorHint')}</p>
                <button onClick={() => refetch()} className="px-4 py-2 text-sm font-medium bg-brand-600 text-white rounded-lg hover:bg-brand-700">{t('common.retry')}</button>
              </div>
            ) : isLoading ? (
              <div className="space-y-3">
                {[1, 2, 3].map(i => (
                  <div key={i} className="bg-white dark:bg-slate-900 border border-gray-200 dark:border-slate-800 rounded-xl p-4 shadow-sm animate-pulse">
                    <div className="h-4 bg-gray-200 dark:bg-slate-800 rounded w-3/4 mb-3"></div>
                    <div className="h-3 bg-gray-200 dark:bg-slate-800 rounded w-1/2 mb-2"></div>
                    <div className="h-3 bg-gray-200 dark:bg-slate-800 rounded w-1/3"></div>
                  </div>
                ))}
              </div>
            ) : !hasItems ? (
              <div className="bg-white dark:bg-slate-900 border border-dashed border-gray-300 dark:border-slate-700 rounded-xl p-10 text-center">
                <div className="w-12 h-12 rounded-xl bg-gray-50 dark:bg-slate-800 text-gray-400 dark:text-slate-500 flex items-center justify-center mx-auto mb-3">
                  <Clock className="w-6 h-6" />
                </div>
                <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100 mb-1">{t('schedEmail.emptyTitle')}</h3>
                <p className="text-xs text-gray-500 dark:text-slate-400">{t('schedEmail.emptyHint')}</p>
              </div>
            ) : (
              <>
                <div className="space-y-3 overflow-y-auto pr-2 flex-1 min-h-0">
                  {scheduledList.map(s => {
                    const conf = getStatusConfig(s.status, t);
                    const connectionName = connections.find(c => c.id === s.connectionId)?.providerAccountId || 'Unknown Connection';
                    const canCancel = (s.status ?? '').toLowerCase() === 'pending';
                    const sendAtLocal = new Date(s.sendAt).toLocaleString(dateLocale, {
                      day: '2-digit', month: '2-digit', year: 'numeric',
                      hour: '2-digit', minute: '2-digit', hour12: false
                    });

                    return (
                      <div
                        key={s.id}
                        onClick={() => setSelectedEmail(s)}
                        className="bg-white dark:bg-slate-900 border border-gray-200 dark:border-slate-800 rounded-xl p-4 shadow-sm hover:shadow-md hover:border-brand-300 transition-all cursor-pointer group"
                      >
                        <div className="flex items-start justify-between gap-3 mb-2">
                          <h4 className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate group-hover:text-brand-600 transition-colors" title={s.subject}>
                            {s.subject || t('sendEmail.noSubject')}
                          </h4>
                          <span className={`inline-flex items-center space-x-1.5 px-2.5 py-1 rounded-full text-xs font-medium shrink-0 ${conf.bg} ${conf.fg}`}>
                            <span className={`w-1.5 h-1.5 rounded-full ${conf.dot}`}></span>
                            <span>{conf.label}</span>
                          </span>
                        </div>
                        <div className="text-[13px] text-gray-500 dark:text-slate-400 mb-2 truncate" title={s.to.join(', ')}>
                          {t('sendEmail.previewTo')} {s.to.join(', ')}
                        </div>
                        <div className="flex items-center gap-1.5 text-xs text-gray-400 dark:text-slate-500">
                          <Clock className="w-3.5 h-3.5" />
                          <span className="truncate">{sendAtLocal} · Gmail ({connectionName})</span>
                        </div>
                        {s.lastError && (
                          <div className="mt-2 text-xs text-red-600 dark:text-red-400 bg-red-50 dark:bg-red-500/10 p-2 rounded-md">{s.lastError}</div>
                        )}
                        {canCancel && (
                          <div className="mt-3 flex justify-end">
                            <button
                              onClick={(e) => { e.stopPropagation(); cancelMutation.mutate(s.id); }}
                              disabled={cancelMutation.isPending}
                              className="px-3 py-1.5 text-xs font-medium text-red-600 dark:text-red-400 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-md hover:border-red-200 dark:hover:border-red-500/30 hover:bg-red-50 dark:hover:bg-red-500/10 transition-colors disabled:opacity-50"
                            >
                              {cancelMutation.isPending && cancelMutation.variables === s.id ? t('schedEmail.cancelling') : t('schedEmail.cancelShort')}
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
