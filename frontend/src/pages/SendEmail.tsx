import React, { useState } from 'react';
import { useQuery, useMutation } from '@tanstack/react-query';
import { Send, Eye, Pencil } from 'lucide-react';
import toast from 'react-hot-toast';
import DOMPurify from 'dompurify';
import { useSearchParams, useNavigate } from 'react-router-dom';
import { sendEmailApi, fileToAttachmentUpload, MAX_ATTACHMENT_TOTAL_BYTES, type SendEmailRequest, type SaveDraftRequest } from '../lib/sendEmailApi';
import { connectionsApi } from '../lib/connectionsApi';
import { EMAIL_TEMPLATES } from '../lib/emailTemplates';
import { handleApiError } from '../lib/errorUtils';
import { EmailChipsInput } from '../components/EmailChipsInput';
import { RichTextEditor } from '../components/RichTextEditor';
import { AttachmentPicker } from '../components/AttachmentPicker';
import { Select } from '../components/Select';
import { useI18n } from '../hooks/useI18n';
import { itemsApi } from '../lib/itemsApi';

export const SendEmail = () => {
  const { t } = useI18n();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const initialDraftItemId = searchParams.get('draftItemId');

  const [to, setTo] = useState<string[]>([]);
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

  // Load existing draft if present
  const { data: draftItem, isLoading: isLoadingDraft } = useQuery({
    queryKey: ['draft-item', draftItemId],
    queryFn: () => itemsApi.getItemById(draftItemId!),
    enabled: !!draftItemId,
  });

  // Populate state from the loaded draft
  React.useEffect(() => {
    if (draftItem) {
      try {
        const meta = JSON.parse(draftItem.metadataJson || '{}');
        setTo(meta.to || []);
        setCc(meta.cc || []);
        setBcc(meta.bcc || []);
        setSubject(meta.subject || '');
        setBody(meta.bodyHtml || '');
        if (draftItem.connectionId) {
          setConn(draftItem.connectionId);
        }
        // Initialize lastSavedState to prevent immediate double-save
        const initialStateStr = JSON.stringify({
          to: meta.to || [],
          cc: meta.cc || [],
          bcc: meta.bcc || [],
          subject: meta.subject || '',
          body: meta.bodyHtml || '',
          resolvedConn: draftItem.connectionId || resolvedConn,
        });
        setLastSavedState(initialStateStr);
      } catch (e) {
        console.error('Error parsing draft metadataJson', e);
      }
    }
  }, [draftItem]);

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

  // Keep a ref to the latest form values so the debounce effect always sees the newest data
  const latestDataRef = React.useRef({ to, cc, bcc, subject, body, resolvedConn });
  React.useEffect(() => {
    latestDataRef.current = { to, cc, bcc, subject, body, resolvedConn };
  }, [to, cc, bcc, subject, body, resolvedConn]);

  // Save draft mutation
  const saveDraftMutation = useMutation({
    mutationFn: async ({ id, data }: { id: string | null; data: SaveDraftRequest }) => {
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
      if (!draftItemId) {
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

  const triggerSaveDraft = React.useCallback(async () => {
    const { to, cc, bcc, subject, body, resolvedConn } = latestDataRef.current;
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
    };

    saveDraftMutation.mutate({ id: draftItemId, data: payload });
  }, [draftItemId, lastSavedState]);

  // Debounce effect for auto-saving drafts (2.0 seconds)
  React.useEffect(() => {
    if (!resolvedConn) return;
    if (isLoadingDraft) return;

    const timer = setTimeout(() => {
      triggerSaveDraft();
    }, 2000);

    return () => clearTimeout(timer);
  }, [to, cc, bcc, subject, body, resolvedConn, isLoadingDraft, triggerSaveDraft]);

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
      toast.success('Draft discarded');
      setTo([]); setCc([]); setBcc([]); setSubject(''); setBody(''); setTemplate('blank'); setFiles([]);
      setDraftItemId(null);
      window.history.replaceState({}, '', window.location.pathname);
      navigate('/inbox');
    },
    onError: (err) => handleApiError(err, 'Failed to discard draft'),
  });

  const handleDiscard = () => {
    if (window.confirm('Are you sure you want to discard this draft?')) {
      discardMutation.mutate();
    }
  };

  const handleSend = async () => {
    if (to.length === 0) return toast.error(t('sendEmail.needTo'));
    if (!subject.trim()) return toast.error(t('sendEmail.needSubject'));
    if (!body.trim()) return toast.error(t('sendEmail.needBody'));
    if (!resolvedConn) return toast.error(t('sendEmail.needConn'));

    const totalSize = files.reduce((s, f) => s + f.size, 0);
    if (totalSize > MAX_ATTACHMENT_TOTAL_BYTES) return toast.error(t('attach.tooLarge'));

    const attachments = files.length > 0 ? await Promise.all(files.map(fileToAttachmentUpload)) : undefined;

    const payload: SendEmailRequest = {
      connectionId: resolvedConn,
      to, cc, bcc, subject, bodyHtml: composedHtml,
      attachments,
    };
    sendMutation.mutate(payload);
  };

  const labelClass = 'block text-xs font-medium text-gray-500 dark:text-slate-400 mb-1.5';
  const inputClass = 'w-full h-9 px-3 border border-gray-300 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100 dark:placeholder-slate-500 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500 transition-colors';

  if (isLoadingDraft) {
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
                Saving draft...
              </span>
            )}
            {!isSavingDraft && draftItemId && (
              <span className="text-xs text-green-600 dark:text-green-400 font-medium">
                Draft saved
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
          <RichTextEditor value={body} onChange={setBody} placeholder={t('sendEmail.contentPlaceholder')} className="mb-3 shrink-0" />

          <AttachmentPicker files={files} onChange={setFiles} className="shrink-0 mb-4" />

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
                onClick={handleDiscard}
                disabled={discardMutation.isPending}
                className="px-4 py-2.5 rounded-lg text-sm font-semibold border border-red-200 dark:border-red-900/50 hover:bg-red-50 dark:hover:bg-red-950/20 text-red-600 dark:text-red-400 transition-colors disabled:opacity-70 disabled:cursor-not-allowed"
              >
                Discard
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
          </div>
        </div>
      </div>
    </div>
  );
};

