import React, { useState } from 'react';
import { useQuery, useMutation } from '@tanstack/react-query';
import { Send, Eye, Pencil, Trash2, Paperclip, FileText } from 'lucide-react';
import toast from 'react-hot-toast';
import DOMPurify from 'dompurify';
import { useSearchParams, useNavigate } from 'react-router-dom';
import { sendEmailApi, fileToAttachmentUpload, MAX_ATTACHMENT_TOTAL_BYTES, type SendEmailRequest, type SaveDraftRequest } from '../lib/sendEmailApi';
import { connectionsApi } from '../lib/connectionsApi';
import { EMAIL_TEMPLATES } from '../lib/emailTemplates';
import { handleApiError } from '../lib/errorUtils';
import { EmailChipsInput } from '../components/EmailChipsInput';
import { RichTextEditor } from '../components/RichTextEditor';
import { Select } from '../components/Select';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { useI18n } from '../hooks/useI18n';
import { itemsApi } from '../lib/itemsApi';

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export const SendEmail = () => {
  const { t } = useI18n();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const initialDraftItemId = searchParams.get('draftItemId');
  // Prefill từ trang Bạn bè ("Gửi mail cho bạn"): /send-email?to=
  const prefillTo = searchParams.get('to')?.trim() ?? '';

  const [to, setTo] = useState<string[]>(() => (prefillTo ? [prefillTo] : []));
  const [cc, setCc] = useState<string[]>([]);
  const [bcc, setBcc] = useState<string[]>([]);
  const [subject, setSubject] = useState('');
  const [body, setBody] = useState('');
  const [conn, setConn] = useState('');
  const [template, setTemplate] = useState('blank');
  const [includeSignature, setIncludeSignature] = useState(true);
  const [files, setFiles] = useState<File[]>([]);

  const [draftItemId, setDraftItemId] = useState<string | null>(initialDraftItemId);
  const [isSavingDraft, setIsSavingDraft] = useState(false);
  const [lastSavedState, setLastSavedState] = useState<string>('');

  const { data: connections = [] } = useQuery({
    queryKey: ['connections'],
    queryFn: connectionsApi.getConnections,
  });

  const activeGmail = React.useMemo(
    () => connections.filter(c => c.serviceType.toLowerCase() === 'gmail' && c.status.toLowerCase() === 'active'),
    [connections],
  );
  const resolvedConn = conn || activeGmail[0]?.id || '';

  // Load existing draft — metadata local (connectionId + thread linkage).
  const { data: draftItem, isLoading: isLoadingDraft } = useQuery({
    queryKey: ['draft-item', draftItemId],
    queryFn: () => itemsApi.getItemById(draftItemId!),
    enabled: !!draftItemId,
  });

  // NỘI DUNG nháp (subject/body/recipients) sống trên Gmail — metadata local (định dạng sync)
  // KHÔNG chứa body. Fetch thread live để lấy nội dung THẬT của message DRAFT.
  const { data: draftThread, isLoading: isLoadingThread } = useQuery({
    queryKey: ['draft-thread', draftItemId],
    queryFn: () => sendEmailApi.getThread(draftItemId!),
    enabled: !!draftItemId,
    retry: false,
    staleTime: 0,
  });

  const isDraftLoadedRef = React.useRef(false);
  // Giữ liên kết thread của draft reply/forward (threadId + message-id gốc) để MỌI lần auto-save
  // gửi kèm — nếu thiếu, Gmail rebuild MIME sẽ tách draft khỏi thread hội thoại gốc.
  const threadLinkRef = React.useRef<{ threadId?: string | null; inReplyToMessageId?: string | null }>({});

  // Hydrate form 1 lần: ƯU TIÊN nội dung live từ Gmail (draftThread), fallback metadata local.
  // Chờ thread settled (xong/lỗi) rồi mới nạp — tránh hiện form rỗng trước khi có content.
  React.useEffect(() => {
    const threadSettled = !draftItemId || !isLoadingThread;
    if (draftItem && threadSettled && !isDraftLoadedRef.current) {
      try {
        const meta = JSON.parse(draftItem.metadataJson || '{}');
        const draftMsg = draftThread?.messages?.find((m) => m.labels?.includes('DRAFT'));

        threadLinkRef.current = {
          threadId: draftThread?.threadId ?? meta.threadId ?? draftItem.threadId ?? null,
          inReplyToMessageId: meta.rfc822MessageId ?? null,
        };

        const nextTo = draftMsg?.to ?? meta.to ?? [];
        const nextCc = draftMsg?.cc ?? meta.cc ?? [];
        const nextBcc = draftMsg?.bcc ?? meta.bcc ?? [];
        const nextSubject = draftMsg?.subject || meta.subject || '';
        // Nháp text thuần (tạo ngoài app) không có bodyHtml → fallback bodyPlainText (giữ xuống dòng).
        const nextBody = draftMsg?.bodyHtml
          || (draftMsg?.bodyPlainText ? draftMsg.bodyPlainText.replace(/\n/g, '<br/>') : '')
          || meta.bodyHtml || '';

        /* eslint-disable react-hooks/set-state-in-effect -- hydrate form state 1 lần từ draft đã fetch */
        setTo(nextTo);
        setCc(nextCc);
        setBcc(nextBcc);
        setSubject(nextSubject);
        setBody(nextBody);
        if (draftItem.connectionId) {
          setConn(draftItem.connectionId);
        }
        // Initialize lastSavedState to prevent immediate double-save
        setLastSavedState(JSON.stringify({
          to: nextTo,
          cc: nextCc,
          bcc: nextBcc,
          subject: nextSubject,
          body: nextBody,
          resolvedConn: draftItem.connectionId || resolvedConn,
        }));
        /* eslint-enable react-hooks/set-state-in-effect */
        isDraftLoadedRef.current = true;
      } catch (e) {
        console.error('Error hydrating draft', e);
      }
    }
  }, [draftItem, draftThread, isLoadingThread, draftItemId, resolvedConn]);

  // Chữ ký THẬT từ Gmail của connection (rỗng nếu chưa đặt / connection cũ thiếu scope settings.basic).
  const { data: signature = '' } = useQuery({
    queryKey: ['gmail-signature', resolvedConn],
    queryFn: () => sendEmailApi.getSignature(resolvedConn),
    enabled: !!resolvedConn,
    staleTime: 5 * 60 * 1000,
  });

  const composedHtml = includeSignature && signature
    ? `${body}<br><br>${signature}`
    : body;

  const applyTemplate = (id: string) => {
    setTemplate(id);
    const tpl = EMAIL_TEMPLATES.find(t => t.id === id);
    if (!tpl) return;
    setBody(tpl.html);
    if (tpl.subject && !subject.trim()) setSubject(tpl.subject);
  };

  const isDiscardedRef = React.useRef(false);

  // Keep a ref to the latest form values so the debounce effect always sees the newest data
  const latestDataRef = React.useRef({ to, cc, bcc, subject, body, resolvedConn, lastSavedState, draftItemId });
  React.useEffect(() => {
    latestDataRef.current = { to, cc, bcc, subject, body, resolvedConn, lastSavedState, draftItemId };
  }, [to, cc, bcc, subject, body, resolvedConn, lastSavedState, draftItemId]);

  // Save draft mutation
  const saveDraftMutation = useMutation({
    mutationFn: async ({ id, data }: { id: string | null; data: SaveDraftRequest }) => {
      if (isDiscardedRef.current) return null;
      if (id) {
        return sendEmailApi.updateDraft(id, data);
      } else {
        return sendEmailApi.createDraft(data);
      }
    },
    onMutate: () => {
      setIsSavingDraft(true);
    },
    onSuccess: (res) => {
      setIsSavingDraft(false);
      if (res && !draftItemId) {
        setDraftItemId(res.id);
        const params = new URLSearchParams(window.location.search);
        params.set('draftItemId', res.id);
        window.history.replaceState({}, '', `${window.location.pathname}?${params.toString()}`);
      }
    },
    onError: (err) => {
      setIsSavingDraft(false);
      console.error('Failed to auto-save draft', err);
    }
  });
  // `mutate` referentially stable (TanStack) — dùng làm dep của useCallback thay cả object mutation.
  const { mutate: mutateSaveDraft } = saveDraftMutation;

  const triggerSaveDraft = React.useCallback(async () => {
    if (isDiscardedRef.current) return;
    const { to, cc, bcc, subject, body, resolvedConn, draftItemId, lastSavedState } = latestDataRef.current;
    if (!resolvedConn) return;
    
    // Check if anything has actually changed from the last saved state
    const currentStateStr = JSON.stringify({ to, cc, bcc, subject, body, resolvedConn });
    if (currentStateStr === lastSavedState) return;

    // Don't auto-save a completely blank draft
    if (to.length === 0 && !subject.trim() && !body.trim()) return;

    setLastSavedState(currentStateStr);
    
    const payload: SaveDraftRequest = {
      connectionId: resolvedConn,
      to,
      cc,
      bcc,
      subject,
      bodyHtml: body,
      threadId: threadLinkRef.current.threadId,
      inReplyToMessageId: threadLinkRef.current.inReplyToMessageId,
    };

    mutateSaveDraft({ id: draftItemId, data: payload }); // mọi giá trị form đọc qua latestDataRef — không cần dep
  }, [mutateSaveDraft]);

  const triggerSaveDraftImmediate = React.useCallback(() => {
    if (isDiscardedRef.current) return;
    const { to, cc, bcc, subject, body, resolvedConn, lastSavedState, draftItemId } = latestDataRef.current;
    if (!resolvedConn) return;

    const currentStateStr = JSON.stringify({ to, cc, bcc, subject, body, resolvedConn });
    if (currentStateStr === lastSavedState) return;

    if (to.length === 0 && !subject.trim() && !body.trim()) return;

    const payload: SaveDraftRequest = {
      connectionId: resolvedConn,
      to,
      cc,
      bcc,
      subject,
      bodyHtml: body,
      threadId: threadLinkRef.current.threadId,
      inReplyToMessageId: threadLinkRef.current.inReplyToMessageId,
    };

    if (draftItemId) {
      sendEmailApi.updateDraft(draftItemId, payload).catch(err => console.error(err));
    } else {
      sendEmailApi.createDraft(payload).catch(err => console.error(err));
    }
  }, []);

  // Debounce effect for auto-saving drafts (2.0 seconds)
  React.useEffect(() => {
    if (!resolvedConn) return;
    if (isLoadingDraft) return;

    const timer = setTimeout(() => {
      triggerSaveDraft();
    }, 2000);

    return () => clearTimeout(timer);
  }, [to, cc, bcc, subject, body, resolvedConn, isLoadingDraft, triggerSaveDraft]);

  // Save on unmount if changed
  React.useEffect(() => {
    return () => {
      triggerSaveDraftImmediate();
    };
  }, [triggerSaveDraftImmediate]);

  const sendMutation = useMutation({
    mutationFn: async (payload: SendEmailRequest) => {
      if (draftItemId) {
        // Save draft one final time with latest composed content (including signature) before sending
        const draftPayload: SaveDraftRequest = {
          connectionId: payload.connectionId,
          to: payload.to,
          cc: payload.cc,
          bcc: payload.bcc,
          subject: payload.subject,
          bodyHtml: composedHtml,
          threadId: threadLinkRef.current.threadId,
          inReplyToMessageId: threadLinkRef.current.inReplyToMessageId,
          // Phải kèm attachment vào lần lưu nháp CUỐI trước khi gửi: nhánh này chạy mỗi khi
          // đã auto-save nháp, mà trước đây bỏ qua attachments của payload → gửi đi mất sạch
          // tệp đính kèm dù UI vẫn hiện đã chọn.
          attachments: payload.attachments,
        };
        await sendEmailApi.updateDraft(draftItemId, draftPayload);
        return sendEmailApi.sendDraft(draftItemId);
      } else {
        return sendEmailApi.send(payload);
      }
    },
    onSuccess: () => {
      toast.success(t('sendEmail.sent'));
      setTo([]); setCc([]); setBcc([]); setSubject(''); setBody(''); setTemplate('blank'); setFiles([]);
      setDraftItemId(null);
      window.history.replaceState({}, '', window.location.pathname);
    },
    onError: (err) => handleApiError(err, t('sendEmail.sendFail')),
  });

  const discardMutation = useMutation({
    mutationFn: () => sendEmailApi.discardDraft(draftItemId!),
    onSuccess: () => {
      toast.success(t('sendEmail.draftDiscarded'));
      setTo([]); setCc([]); setBcc([]); setSubject(''); setBody(''); setTemplate('blank'); setFiles([]);
      setDraftItemId(null);
      window.history.replaceState({}, '', window.location.pathname);
      navigate('/inbox');
    },
    onError: (err) => handleApiError(err, 'Failed to discard draft'),
  });

  const [discardConfirmOpen, setDiscardConfirmOpen] = useState(false);

  const handleDiscard = () => {
    isDiscardedRef.current = true;
    discardMutation.mutate(undefined, { onSettled: () => setDiscardConfirmOpen(false) });
  };

  const handleSend = async () => {
    if (to.length === 0) return toast.error(t('sendEmail.needTo'));
    if (!subject.trim()) return toast.error(t('sendEmail.needSubject'));
    if (!body.trim()) return toast.error(t('sendEmail.needBody'));
    if (!resolvedConn) return toast.error(t('sendEmail.needConn'));

    const totalSize = files.reduce((s, f) => s + f.size, 0);
    if (totalSize > MAX_ATTACHMENT_TOTAL_BYTES) return toast.error(t('attach.tooLarge'));

    const attachments = files.length > 0 ? await Promise.all(files.map(fileToAttachmentUpload)) : undefined;

    isDiscardedRef.current = true;
    const payload: SendEmailRequest = {
      connectionId: resolvedConn,
      to, cc, bcc, subject, bodyHtml: composedHtml,
      attachments,
    };
    sendMutation.mutate(payload);
  };

  const labelClass = 'block text-xs font-medium text-gray-500 dark:text-slate-400 mb-1.5';
  const inputClass = 'w-full h-9 px-3 border border-gray-300 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100 dark:placeholder-slate-500 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500 transition-colors';

  if (isLoadingDraft || (!!draftItemId && isLoadingThread)) {
    return (
      <div className="h-[calc(100vh-64px)] flex items-center justify-center bg-gray-50 dark:bg-slate-950">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-brand-600"></div>
      </div>
    );
  }

  return (
    <div className="p-5 md:p-8 max-w-[1600px] mx-auto h-[calc(100vh-64px)] flex flex-col overflow-hidden">
      <div className="mb-6 shrink-0">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100 mb-1">{t('sendEmail.title')}</h1>
        <p className="text-sm text-gray-500 dark:text-slate-400">{t('sendEmail.subtitle')}</p>
      </div>

      <div className="flex flex-col lg:flex-row gap-6 items-stretch flex-1 min-h-0">
        {/* Compose */}
        <div className="flex-1 w-full lg:w-1/2 flex flex-col min-h-0">
          <div className="flex items-center justify-between mb-3.5 shrink-0">
            <div className="flex items-center gap-2 text-gray-900 dark:text-slate-100">
              <Pencil className="w-4 h-4 text-gray-400 dark:text-slate-500" />
              <h2 className="text-base font-semibold">{t('sendEmail.compose')}</h2>
            </div>
            {isSavingDraft && (
              <span className="text-xs text-gray-400 dark:text-slate-500 animate-pulse">
                {t('sendEmail.savingDraft')}
              </span>
            )}
            {!isSavingDraft && draftItemId && (
              <span className="text-xs text-green-600 dark:text-green-400 font-medium">
                {t('sendEmail.draftSaved')}
              </span>
            )}
          </div>
          <div className="flex-1 min-h-0 bg-white dark:bg-slate-900 border border-gray-200 dark:border-slate-800 rounded-xl p-5 md:p-6 shadow-sm flex flex-col overflow-y-auto">
          <label className={`${labelClass} shrink-0`}>{t('sendEmail.to')}</label>
          <div className="shrink-0">
            <EmailChipsInput value={to} onChange={setTo} connectionId={resolvedConn || undefined} placeholder={t('sendEmail.toPlaceholder')} />
          </div>

          <div className="flex flex-col sm:flex-row sm:gap-3 shrink-0">
            <div className="flex-1">
              <label className={labelClass}>Cc</label>
              <EmailChipsInput value={cc} onChange={setCc} connectionId={resolvedConn || undefined} placeholder="email@..." />
            </div>
            <div className="flex-1">
              <label className={labelClass}>Bcc</label>
              <EmailChipsInput value={bcc} onChange={setBcc} connectionId={resolvedConn || undefined} placeholder="email@..." />
            </div>
          </div>

          <div className="flex flex-col sm:flex-row sm:gap-3 shrink-0">
            <div className="flex-1">
              <label className={labelClass}>{t('sendEmail.subject')}</label>
              <input value={subject} onChange={(e) => setSubject(e.target.value)} placeholder={t('sendEmail.subjectPlaceholder')} className={`${inputClass} mb-3`} />
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
            value={body}
            onChange={setBody}
            placeholder={t('sendEmail.contentPlaceholder')}
            className="mb-4 shrink-0"
            attachFiles={files}
            onAttachFilesChange={setFiles}
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

          <label className={`${labelClass} shrink-0`}>{t('sendEmail.connection')}</label>
          <div className="shrink-0 mb-4">
            <Select
              value={resolvedConn}
              onChange={setConn}
              options={activeGmail.map(c => ({ value: c.id, label: `Gmail · ${c.providerAccountId}` }))}
              placeholder={t('sendEmail.connectionPlaceholder')}
              className="h-9"
            />
          </div>

          <div className="flex gap-3 mt-2 shrink-0">
            <button
              onClick={handleSend}
              disabled={sendMutation.isPending || saveDraftMutation.isPending}
              className="flex-1 flex items-center justify-center space-x-2 py-2.5 rounded-lg text-sm font-semibold bg-brand-600 hover:bg-brand-700 text-white transition-colors disabled:opacity-70 disabled:cursor-not-allowed"
            >
              <Send className="w-4 h-4" />
              <span>{sendMutation.isPending ? t('sendEmail.sending') : t('sendEmail.sendNow')}</span>
            </button>
            {draftItemId && (
              <button
                type="button"
                onClick={() => setDiscardConfirmOpen(true)}
                disabled={discardMutation.isPending}
                className="flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium text-rose-600 dark:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-500/10 rounded-md transition-colors disabled:opacity-50"
              >
                <Trash2 className="w-4 h-4" />
                {t('sendEmail.discardDraft')}
              </button>
            )}
          </div>
          </div>
        </div>

        {/* Preview */}
        <div className="flex-1 w-full lg:w-1/2 flex flex-col min-h-0">
          <div className="flex items-center gap-2 mb-3.5 shrink-0 text-gray-900 dark:text-slate-100">
            <Eye className="w-4 h-4 text-gray-400 dark:text-slate-500" />
            <h2 className="text-base font-semibold">{t('sendEmail.preview')}</h2>
          </div>
          <div className="flex-1 min-h-0 bg-white dark:bg-slate-900 border border-gray-200 dark:border-slate-800 rounded-xl shadow-sm overflow-hidden flex flex-col">
            <div className="px-5 py-3 border-b border-gray-100 dark:border-slate-800 shrink-0">
              <p className="text-xs text-gray-400 dark:text-slate-500">{t('sendEmail.subject')}</p>
              <p className="text-sm font-semibold text-gray-900 dark:text-slate-100 truncate">{subject || t('sendEmail.noSubject')}</p>
              {to.length > 0 && (
                <p className="text-xs text-gray-400 dark:text-slate-500 mt-1 truncate">{t('sendEmail.previewTo')} {to.join(', ')}</p>
              )}
              {cc.length > 0 && (
                <p className="text-xs text-gray-400 dark:text-slate-500 mt-0.5 truncate">Cc: {cc.join(', ')}</p>
              )}
              {bcc.length > 0 && (
                <p className="text-xs text-gray-400 dark:text-slate-500 mt-0.5 truncate">Bcc: {bcc.join(', ')}</p>
              )}
            </div>
            <div className="flex-1 overflow-y-auto">
              {body.trim() ? (
                <div className="html-content px-5 py-4" dangerouslySetInnerHTML={{ __html: DOMPurify.sanitize(composedHtml) }} />
              ) : (
                <div className="h-full flex items-center justify-center text-sm text-gray-400 dark:text-slate-500 p-8 text-center">
                  {t('sendEmail.emptyPreview')}
                </div>
              )}
            </div>

            {/* Đính kèm — hiện đúng như thư sẽ gửi */}
            {files.length > 0 && (
              <div className="shrink-0 border-t border-gray-100 dark:border-slate-800 px-5 py-3">
                <p className="flex items-center gap-1.5 text-xs font-semibold text-gray-500 dark:text-slate-400 mb-2">
                  <Paperclip className="w-3.5 h-3.5" />
                  {t('sendEmail.previewAttachments', { n: files.length })}
                </p>
                <div className="flex flex-wrap gap-2">
                  {files.map((f, i) => (
                    <span key={`${f.name}::${f.size}::${i}`} className="inline-flex items-center gap-1.5 max-w-[220px] px-2 py-1 rounded-lg border border-gray-200 dark:border-slate-700 bg-gray-50 dark:bg-slate-800 text-[12px]">
                      <FileText className="w-3.5 h-3.5 shrink-0 text-gray-400 dark:text-slate-500" />
                      <span className="truncate text-gray-700 dark:text-slate-200">{f.name}</span>
                      <span className="shrink-0 text-gray-400 dark:text-slate-500">{formatFileSize(f.size)}</span>
                    </span>
                  ))}
                </div>
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Xác nhận hủy thư nháp */}
      <ConfirmDialog
        open={discardConfirmOpen}
        tone="danger"
        message={t('sendEmail.discardConfirm')}
        confirmLabel={t('sendEmail.discardDraft')}
        loading={discardMutation.isPending}
        onConfirm={handleDiscard}
        onCancel={() => setDiscardConfirmOpen(false)}
      />
    </div>
  );
};

