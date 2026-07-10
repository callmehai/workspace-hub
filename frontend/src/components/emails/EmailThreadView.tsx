import React, { useState, useEffect, useRef } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Paperclip, Download, ChevronDown, ChevronRight, Reply, ReplyAll, Forward, Loader2, Send, X } from 'lucide-react';
import { sendEmailApi, fileToAttachmentUpload, MAX_ATTACHMENT_TOTAL_BYTES, type EmailAttachmentDto, type EmailThreadMessageDto } from '../../lib/sendEmailApi';
import { connectionsApi } from '../../lib/connectionsApi';
import { EmailChipsInput } from '../EmailChipsInput';
import { RichTextEditor } from '../RichTextEditor';
import { AttachmentPicker } from '../AttachmentPicker';
import { AttachmentCard } from './AttachmentCard';
import { useI18n } from '../../hooks/useI18n';
import toast from 'react-hot-toast';
import DOMPurify from 'dompurify';

interface EmailThreadViewProps {
  itemId: string;
  connectionId: string;
}

// Gmail API không trả ảnh đại diện → dựng avatar chữ-cái-đầu, màu ổn định theo người gửi (kiểu Gmail).
const AVATAR_COLORS = [
  'bg-blue-500', 'bg-emerald-500', 'bg-violet-500', 'bg-amber-500', 'bg-rose-500',
  'bg-cyan-500', 'bg-indigo-500', 'bg-teal-500', 'bg-orange-500', 'bg-pink-500',
];
function avatarColor(key: string): string {
  let h = 0;
  for (let i = 0; i < key.length; i++) h = (h * 31 + key.charCodeAt(i)) >>> 0;
  return AVATAR_COLORS[h % AVATAR_COLORS.length];
}

/** Tách "Tên <email>" → tên hiển thị + email. Không có tên hiển thị thì dùng email làm tên. */
function parseSender(from?: string | null): { name: string; email: string } {
  if (!from) return { name: 'Unknown', email: '' };
  const m = from.match(/<([^>]+)>/);
  const email = (m ? m[1] : from).trim();
  const name = from.split('<')[0].trim().replace(/^["']|["']$/g, '') || email || 'Unknown';
  return { name, email };
}

function extractEmail(str?: string | null): string {
  if (!str) return '';
  const m = str.match(/<([^>]+)>/);
  return (m ? m[1] : str).trim().toLowerCase();
}

export const EmailThreadView: React.FC<EmailThreadViewProps> = ({ itemId, connectionId }) => {
  const { t, lang } = useI18n();
  const dl = lang === 'vi' ? 'vi-VN' : 'en-US';
  const queryClient = useQueryClient();

  // undefined = chưa bấm → mặc định (thư mới nhất mở, còn lại thu gọn); true/false = user đã toggle.
  const [expandedMsgs, setExpandedMsgs] = useState<Record<string, boolean | undefined>>({});
  const [replyMode, setReplyMode] = useState<'reply' | 'replyAll' | 'forward' | null>(null);
  const isDraftLoadedRef = useRef(false);

  const [to, setTo] = useState<string[]>([]);
  const [cc, setCc] = useState<string[]>([]);
  const [bcc, setBcc] = useState<string[]>([]);
  const [bodyHtml, setBodyHtml] = useState<string>('');
  const [includeAttachments, setIncludeAttachments] = useState(true);
  const [attachFiles, setAttachFiles] = useState<File[]>([]);

  // States for drafts
  const [draftItemId, setDraftItemId] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [lastSavedState, setLastSavedState] = useState<string>('');
  const [isDraftClosed, setIsDraftClosed] = useState(false);

  const { data: thread, isLoading, isError } = useQuery({
    queryKey: ['emailThread', itemId],
    queryFn: () => sendEmailApi.getThread(itemId),
  });

  const { data: connections = [] } = useQuery({
    queryKey: ['connections'],
    queryFn: connectionsApi.getConnections,
  });

  const currentConnection = connections.find(c => c.id === connectionId);
  const me = currentConnection?.providerAccountId || '';

  const getReplyRecipients = (mode: 'reply' | 'replyAll' | 'forward', msg: EmailThreadMessageDto, myEmailAddr: string) => {
    const toSet = new Set<string>();
    const ccSet = new Set<string>();
    
    const sender = msg.from || '';
    const senderEmail = extractEmail(sender);
    const myEmail = myEmailAddr.toLowerCase();

    if (mode === 'reply') {
      if (senderEmail && senderEmail !== myEmail) {
        toSet.add(sender);
      } else {
        const firstTo = msg.to?.[0];
        if (firstTo) toSet.add(firstTo);
      }
    } else if (mode === 'replyAll') {
      if (senderEmail && senderEmail !== myEmail) {
        toSet.add(sender);
      }
      
      msg.to?.forEach((t: string) => {
        const e = extractEmail(t);
        if (e && e !== myEmail) {
          toSet.add(t);
        }
      });

      msg.cc?.forEach((c: string) => {
        const e = extractEmail(c);
        if (e && e !== myEmail) {
          ccSet.add(c);
        }
      });
    }

    return {
      to: Array.from(toSet),
      cc: Array.from(ccSet),
      bcc: [] as string[]
    };
  };

  // Load existing draft if present in the thread on initial load.
  // Hydrate form state 1 lần từ draft đã fetch (guard isDraftLoadedRef) — chủ đích, nên tắt set-state-in-effect.
  /* eslint-disable react-hooks/set-state-in-effect */
  useEffect(() => {
    if (replyMode === null && !isDraftClosed && thread && thread.messages && !isDraftLoadedRef.current) {
      const draft = thread.messages.find(m => m.labels?.includes('DRAFT'));
      if (draft) {
        setDraftItemId(draft.itemId || null);
        setTo(draft.to || []);
        setCc(draft.cc || []);
        setBcc(draft.bcc || []);
        setBodyHtml(draft.bodyHtml || '');
        setLastSavedState(JSON.stringify({
          to: draft.to || [],
          cc: draft.cc || [],
          bcc: draft.bcc || [],
          bodyHtml: draft.bodyHtml || ''
        }));
        
        if (draft.subject?.toLowerCase().startsWith('fwd:')) {
          setReplyMode('forward');
        } else if (draft.cc?.length > 0 || draft.to?.length > 1) {
          setReplyMode('replyAll');
        } else {
          setReplyMode('reply');
        }
        isDraftLoadedRef.current = true;
      }
    }
  }, [thread, replyMode, isDraftClosed]);
  /* eslint-enable react-hooks/set-state-in-effect */

  const isDiscardedRef = useRef(false);

  // Keep latest data in a ref for the debounced auto-save
  const latestDataRef = useRef({ to, cc, bcc, bodyHtml, replyMode, draftItemId, lastSavedState });
  useEffect(() => {
    latestDataRef.current = { to, cc, bcc, bodyHtml, replyMode, draftItemId, lastSavedState };
  }, [to, cc, bcc, bodyHtml, replyMode, draftItemId, lastSavedState]);

  const triggerAutoSave = React.useCallback(async () => {
    if (isDiscardedRef.current) return;
    const { to, cc, bcc, bodyHtml, replyMode, draftItemId, lastSavedState } = latestDataRef.current;
    if (!replyMode || !thread) return;

    // Check if anything has changed
    const currentStateStr = JSON.stringify({ to, cc, bcc, bodyHtml });
    if (currentStateStr === lastSavedState) return;

    // Check if discarded in the meantime
    if (latestDataRef.current.replyMode === null) return;

    setIsSaving(true);
    try {
      const baseSubject = thread.subject || 'No Subject';
      const draftSubject = replyMode === 'forward'
        ? (baseSubject.toLowerCase().startsWith('fwd:') ? baseSubject : `Fwd: ${baseSubject}`)
        : (baseSubject.toLowerCase().startsWith('re:') ? baseSubject : `Re: ${baseSubject}`);

      const latestMsg = thread.messages[thread.messages.length - 1];

      const payload = {
        connectionId,
        to,
        cc,
        bcc,
        subject: draftSubject,
        bodyHtml,
        threadId: thread.threadId,
        inReplyToMessageId: latestMsg?.messageId || undefined,
      };

      if (draftItemId) {
        await sendEmailApi.updateDraft(draftItemId, payload);
        if (latestDataRef.current.replyMode === null || isDiscardedRef.current) return;
        setLastSavedState(currentStateStr);
      } else {
        const savedDraft = await sendEmailApi.createDraft(payload);
        if (latestDataRef.current.replyMode === null || isDiscardedRef.current) {
          await sendEmailApi.discardDraft(savedDraft.id);
          return;
        }
        setDraftItemId(savedDraft.id);
        setLastSavedState(currentStateStr);
      }
    } catch (err) {
      console.error('Error auto-saving reply draft:', err);
    } finally {
      setIsSaving(false);
    }
  }, [thread, connectionId]);

  // Debounced auto-save effect (2s) — reset timer khi nội dung/replyMode/triggerAutoSave đổi.
  useEffect(() => {
    if (!replyMode) return;

    const timer = setTimeout(() => {
      triggerAutoSave();
    }, 2000);

    return () => clearTimeout(timer);
  }, [to, cc, bcc, bodyHtml, replyMode, triggerAutoSave]);

  const triggerAutoSaveImmediate = React.useCallback(() => {
    if (isDiscardedRef.current) return;
    const { to, cc, bcc, bodyHtml, replyMode, draftItemId, lastSavedState } = latestDataRef.current;
    if (!replyMode || !thread) return;

    const currentStateStr = JSON.stringify({ to, cc, bcc, bodyHtml });
    if (currentStateStr === lastSavedState) return;

    const baseSubject = thread.subject || 'No Subject';
    const draftSubject = replyMode === 'forward'
      ? (baseSubject.toLowerCase().startsWith('fwd:') ? baseSubject : `Fwd: ${baseSubject}`)
      : (baseSubject.toLowerCase().startsWith('re:') ? baseSubject : `Re: ${baseSubject}`);

    const latestMsg = thread.messages[thread.messages.length - 1];

    const payload = {
      connectionId,
      to,
      cc,
      bcc,
      subject: draftSubject,
      bodyHtml,
      threadId: thread.threadId,
      inReplyToMessageId: latestMsg?.messageId || undefined,
    };

    if (draftItemId) {
      sendEmailApi.updateDraft(draftItemId, payload).catch(err => console.error(err));
    } else {
      sendEmailApi.createDraft(payload).catch(err => console.error(err));
    }
  }, [thread, connectionId]);

  // Save on unmount if changed
  useEffect(() => {
    return () => {
      if (latestDataRef.current.replyMode !== null) {
        triggerAutoSaveImmediate();
      }
    };
  }, [triggerAutoSaveImmediate]);

  const toggleMsg = (msgId: string, currentlyExpanded: boolean) => {
    setExpandedMsgs(prev => ({ ...prev, [msgId]: !currentlyExpanded }));
  };

  const handleAction = (mode: 'reply' | 'replyAll' | 'forward') => {
    isDiscardedRef.current = false;
    setIsDraftClosed(false);
    setIncludeAttachments(true);

    // Đang có draft dở của thread này (vừa đóng bằng nút X, chưa gửi/huỷ) → RESUME đúng draft đó:
    // giữ nguyên nội dung + draftItemId, KHÔNG tạo draft mới → tránh bỏ rơi draft cũ trên Gmail.
    if (draftItemId) {
      setReplyMode(mode);
      setTimeout(() => {
        document.getElementById('reply-box')?.scrollIntoView({ behavior: 'smooth' });
      }, 100);
      return;
    }

    setReplyMode(mode);
    setBodyHtml('');
    setDraftItemId(null);
    setLastSavedState('');
    setAttachFiles([]);

    const latestMsg = thread?.messages?.[thread.messages.length - 1];
    if (latestMsg) {
      const rec = getReplyRecipients(mode, latestMsg, me);
      setTo(rec.to);
      setCc(rec.cc);
      setBcc(rec.bcc);
    } else {
      setTo([]);
      setCc([]);
      setBcc([]);
    }

    // Cuộn xuống box
    setTimeout(() => {
      document.getElementById('reply-box')?.scrollIntoView({ behavior: 'smooth' });
    }, 100);
  };

  const replyMutation = useMutation({
    mutationFn: async (mode: 'reply' | 'replyAll' | 'forward') => {
      const { to, cc, bcc, bodyHtml, draftItemId } = latestDataRef.current;

      const attachments = attachFiles.length > 0
        ? await Promise.all(attachFiles.map(fileToAttachmentUpload))
        : undefined;

      if (draftItemId) {
        // Save draft one last time before sending
        const baseSubject = thread?.subject || 'No Subject';
        const draftSubject = mode === 'forward'
          ? (baseSubject.toLowerCase().startsWith('fwd:') ? baseSubject : `Fwd: ${baseSubject}`)
          : (baseSubject.toLowerCase().startsWith('re:') ? baseSubject : `Re: ${baseSubject}`);
        const latestMsg = thread?.messages?.[thread.messages.length - 1];

        await sendEmailApi.updateDraft(draftItemId, {
          connectionId,
          to,
          cc,
          bcc,
          subject: draftSubject,
          bodyHtml,
          threadId: thread?.threadId,
          inReplyToMessageId: latestMsg?.messageId || undefined,
        });

        // Send draft
        return sendEmailApi.sendDraft(draftItemId);
      } else {
        if (mode === 'forward') {
          return sendEmailApi.forward({
            connectionId,
            itemId,
            to,
            cc,
            bcc,
            bodyHtml,
            includeAttachments,
            attachments
          });
        } else {
          return sendEmailApi.reply({
            connectionId,
            itemId,
            cc,
            bcc,
            bodyHtml,
            replyAll: mode === 'replyAll',
            attachments
          });
        }
      }
    },
    onMutate: () => {
      isDiscardedRef.current = true;
    },
    onSuccess: () => {
      toast.success(t('item.saved') || 'Sent successfully');
      setReplyMode(null);
      setDraftItemId(null);
      setBodyHtml('');
      setTo([]);
      setCc([]);
      setBcc([]);
      setAttachFiles([]);
      setLastSavedState('');
      queryClient.invalidateQueries({ queryKey: ['emailThread', itemId] });
      queryClient.invalidateQueries({ queryKey: ['items'] });
    },
    onError: (err) => {
      isDiscardedRef.current = false;
      toast.error(t('item.saveFail') || 'Failed to send email');
      console.error(err);
    }
  });

  const discardMutation = useMutation({
    mutationFn: async () => {
      isDiscardedRef.current = true;
      if (draftItemId) {
        await sendEmailApi.discardDraft(draftItemId);
      }
    },
    onSuccess: () => {
      toast.success(t('sendEmail.draftDiscarded'));
      setReplyMode(null);
      setDraftItemId(null);
      setBodyHtml('');
      setTo([]);
      setCc([]);
      setBcc([]);
      setAttachFiles([]);
      setLastSavedState('');
      queryClient.invalidateQueries({ queryKey: ['emailThread', itemId] });
      queryClient.invalidateQueries({ queryKey: ['items'] });
    },
    onError: (err) => {
      isDiscardedRef.current = false;
      toast.error(t('item.saveFail') || 'Failed to discard draft');
      console.error(err);
    }
  });

  const sendAction = () => {
    if (!replyMode) return;
    if (replyMode === 'forward' && to.length === 0) {
      toast.error(t('item.eventNeedFields') || 'To field is required for forward');
      return;
    }
    const totalSize = attachFiles.reduce((s, f) => s + f.size, 0);
    if (totalSize > MAX_ATTACHMENT_TOTAL_BYTES) {
      toast.error(t('attach.tooLarge'));
      return;
    }
    replyMutation.mutate(replyMode);
  };

  const [downloadingAllMsgId, setDownloadingAllMsgId] = useState<string | null>(null);

  const downloadAttachment = async (msgId: string, att: EmailAttachmentDto) => {
    try {
      toast.loading(t('common.loading') || 'Downloading...', { id: `dl-${att.attachmentId}` });
      await sendEmailApi.downloadAttachment(itemId, msgId, att.attachmentId, att.filename, att.mimeType);
      toast.success(t('item.saved') || 'Downloaded', { id: `dl-${att.attachmentId}` });
    } catch {
      toast.error(t('item.loadError') || 'Failed to download', { id: `dl-${att.attachmentId}` });
    }
  };

  const downloadAll = async (msgId: string) => {
    setDownloadingAllMsgId(msgId);
    try {
      await sendEmailApi.downloadAllAttachments(itemId, msgId);
    } catch {
      toast.error(t('item.loadError') || 'Failed to download');
    } finally {
      setDownloadingAllMsgId(null);
    }
  };

  if (isLoading) {
    return (
      <div className="flex justify-center items-center py-8">
        <Loader2 className="w-6 h-6 animate-spin text-brand-500" />
      </div>
    );
  }

  if (isError || !thread) {
    return (
      <div className="p-4 bg-rose-50 dark:bg-rose-500/10 text-rose-600 dark:text-rose-400 rounded-lg text-sm">
        {t('item.loadError')}
      </div>
    );
  }

  // Filter out any messages that are draft
  const nonDraftMessages = thread.messages.filter(msg => !msg.labels?.includes('DRAFT'));

  return (
    <div className="space-y-4">
      <div className="text-[11px] font-semibold tracking-[0.04em] uppercase text-slate-400 dark:text-slate-500 mb-2">
        {t('sendEmail.content') || 'Hội thoại'} ({nonDraftMessages.length})
      </div>
      
      <div className="space-y-3">
        {nonDraftMessages.map((msg, index) => {
          const isLatest = index === nonDraftMessages.length - 1;
          const isExpanded = expandedMsgs[msg.messageId] ?? isLatest;
          const sender = parseSender(msg.from);
          const initial = (sender.name || sender.email || '?').charAt(0).toUpperCase();

          return (
            <div key={msg.messageId} className="border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden bg-white dark:bg-slate-900 shadow-sm transition-all">
              {/* Header của từng message */}
              <button
                onClick={() => toggleMsg(msg.messageId, isExpanded)}
                className="w-full flex items-center justify-between px-4 py-3 bg-slate-50 hover:bg-slate-100 dark:bg-slate-800 dark:hover:bg-slate-700/80 transition-colors"
              >
                <div className="flex items-center gap-3 overflow-hidden">
                  <div className={`w-8 h-8 rounded-full ${avatarColor(sender.email || sender.name)} text-white flex items-center justify-center font-bold text-sm shrink-0 shadow-sm`}>
                    {initial}
                  </div>
                  <div className="flex flex-col items-start min-w-0">
                    <span className="text-sm font-semibold text-slate-800 dark:text-slate-100 truncate max-w-full" title={sender.email}>{sender.name}</span>
                    <span className="text-xs text-slate-500 dark:text-slate-400 truncate">
                      {new Date(msg.occurredAt).toLocaleString(dl)}
                    </span>
                  </div>
                </div>
                <div className="flex items-center gap-3 shrink-0 text-slate-400">
                  {msg.hasAttachment && <Paperclip className="w-4 h-4" />}
                  {isExpanded ? <ChevronDown className="w-5 h-5" /> : <ChevronRight className="w-5 h-5" />}
                </div>
              </button>

              {/* Nội dung message */}
              {isExpanded && (
                <div className="px-4 py-4 border-t border-slate-200 dark:border-slate-800">
                  {/* To, Cc, Bcc Info */}
                  <div className="mb-4 space-y-1 text-[12px] text-slate-600 dark:text-slate-400 bg-slate-50 dark:bg-slate-800/50 p-2.5 rounded-lg">
                    {msg.from && <div><span className="font-semibold w-10 inline-block">From:</span> {msg.from}</div>}
                    {msg.to?.length > 0 && <div><span className="font-semibold w-10 inline-block">To:</span> {msg.to.join(', ')}</div>}
                    {msg.cc?.length > 0 && <div><span className="font-semibold w-10 inline-block">Cc:</span> {msg.cc.join(', ')}</div>}
                    {msg.bcc?.length > 0 && <div><span className="font-semibold w-10 inline-block">Bcc:</span> {msg.bcc.join(', ')}</div>}
                  </div>

                  {/* HTML Body — LUÔN render trên nền sáng: HTML người gửi soạn cho nền trắng,
                      để nền tối (dark mode) sẽ thành chữ đen trên nền tối = vô hình. */}
                  <div
                    className="text-[13.5px] leading-[1.65] break-words overflow-x-auto email-body-content rounded-lg bg-white text-slate-900 p-3.5 ring-1 ring-slate-200 dark:ring-slate-700 [color-scheme:light]"
                    dangerouslySetInnerHTML={{ __html: DOMPurify.sanitize(msg.bodyHtml || msg.bodyPlainText?.replace(/\n/g, '<br/>') || '') }}
                  />

                  {/* Attachments */}
                  {msg.attachments?.length > 0 && (
                    <div className="mt-4 pt-4 border-t border-slate-100 dark:border-slate-800/60">
                      <div className="text-xs font-semibold text-slate-500 mb-2 flex items-center justify-between">
                        <span className="flex items-center gap-1.5">
                          <Paperclip className="w-3.5 h-3.5" /> Attachments ({msg.attachments.length})
                        </span>
                        {msg.attachments.length > 1 && (
                          <button
                            onClick={() => downloadAll(msg.messageId)}
                            disabled={downloadingAllMsgId === msg.messageId}
                            className="inline-flex items-center gap-1.5 px-2 py-1 rounded text-[11.5px] font-medium text-brand-600 dark:text-brand-400 hover:bg-brand-50 dark:hover:bg-brand-500/10 transition-colors disabled:opacity-50"
                          >
                            {downloadingAllMsgId === msg.messageId
                              ? <Loader2 className="w-3.5 h-3.5 animate-spin" />
                              : <Download className="w-3.5 h-3.5" />}
                            {t('attach.downloadAll')}
                          </button>
                        )}
                      </div>
                      <div className="flex flex-wrap gap-2">
                        {msg.attachments.map(att => (
                          <AttachmentCard
                            key={att.attachmentId}
                            itemId={itemId}
                            messageId={msg.messageId}
                            att={att}
                            onDownload={(a) => downloadAttachment(msg.messageId, a)}
                          />
                        ))}
                      </div>
                    </div>
                  )}

                  {/* Quick Actions (only show on latest expanded) */}
                  {isLatest && !replyMode && (
                    <div className="mt-5 flex items-center gap-2">
                      <button onClick={() => handleAction('reply')} className="h-9 px-4 rounded-lg bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-200 hover:bg-slate-200 dark:hover:bg-slate-700 text-sm font-semibold flex items-center gap-1.5 transition-colors">
                        <Reply className="w-4 h-4 text-slate-500" /> Reply
                      </button>
                      <button onClick={() => handleAction('replyAll')} className="h-9 px-4 rounded-lg bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-200 hover:bg-slate-200 dark:hover:bg-slate-700 text-sm font-semibold flex items-center gap-1.5 transition-colors">
                        <ReplyAll className="w-4 h-4 text-slate-500" /> Reply All
                      </button>
                      <button onClick={() => handleAction('forward')} className="h-9 px-4 rounded-lg bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-200 hover:bg-slate-200 dark:hover:bg-slate-700 text-sm font-semibold flex items-center gap-1.5 transition-colors">
                        <Forward className="w-4 h-4 text-slate-500" /> Forward
                      </button>
                    </div>
                  )}
                </div>
              )}
            </div>
          );
        })}
      </div>

      {/* Reply/Forward Box */}
      {replyMode && (
        <div id="reply-box" className="mt-4 border border-brand-200 dark:border-brand-500/30 rounded-xl overflow-hidden shadow-lg animate-in fade-in slide-in-from-bottom-4 duration-300 bg-white dark:bg-slate-900">
          <div className="px-4 py-3 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between bg-brand-50 dark:bg-brand-500/10">
            <h3 className="text-sm font-bold text-brand-800 dark:text-brand-300 flex items-center gap-2">
              {replyMode === 'reply' && <><Reply className="w-4 h-4" /> Reply</>}
              {replyMode === 'replyAll' && <><ReplyAll className="w-4 h-4" /> Reply All</>}
              {replyMode === 'forward' && <><Forward className="w-4 h-4" /> Forward</>}
            </h3>
            <button 
              onClick={() => {
                setReplyMode(null);
                setIsDraftClosed(true);
              }} 
              className="p-1 rounded hover:bg-brand-200/50 dark:hover:bg-brand-500/20 text-brand-700 dark:text-brand-400 transition-colors"
            >
              <X className="w-4 h-4" />
            </button>
          </div>
          
          <div className="p-4 space-y-3">
            {replyMode === 'forward' && (
              <div>
                <label className="block text-xs font-semibold text-slate-500 mb-1">To</label>
                <EmailChipsInput value={to} onChange={setTo} placeholder="Add recipient..." connectionId={connectionId} />
              </div>
            )}
            
            <div className="grid grid-cols-2 gap-3">
              <div className="min-w-0">
                <label className="block text-xs font-semibold text-slate-500 mb-1">Cc</label>
                <EmailChipsInput value={cc} onChange={setCc} placeholder="Add Cc..." connectionId={connectionId} />
              </div>
              <div className="min-w-0">
                <label className="block text-xs font-semibold text-slate-500 mb-1">Bcc</label>
                <EmailChipsInput value={bcc} onChange={setBcc} placeholder="Add Bcc..." connectionId={connectionId} />
              </div>
            </div>

            {replyMode === 'forward' && (
              <label className="flex items-center gap-2 text-sm text-slate-700 dark:text-slate-300 py-1">
                <input 
                  type="checkbox" 
                  checked={includeAttachments} 
                  onChange={(e) => setIncludeAttachments(e.target.checked)} 
                  className="rounded border-slate-300 text-brand-600 focus:ring-brand-500"
                />
                Include original attachments
              </label>
            )}

            <div>
              <label className="block text-xs font-semibold text-slate-500 mb-1">Message</label>
              <RichTextEditor
                value={bodyHtml}
                onChange={setBodyHtml}
                placeholder="Write your message here..."
                className="min-h-[200px]"
              />
            </div>

            <AttachmentPicker files={attachFiles} onChange={setAttachFiles} />

            <div className="flex justify-between items-center pt-2">
              <div className="flex items-center gap-2">
                {draftItemId && (
                  <button
                    onClick={() => {
                      if (window.confirm(t('sendEmail.discardConfirm'))) {
                        discardMutation.mutate();
                      }
                    }}
                    disabled={discardMutation.isPending || replyMutation.isPending}
                    className="h-10 px-4 rounded-lg border border-rose-200 text-rose-600 hover:bg-rose-50 dark:border-rose-500/30 dark:text-rose-400 dark:hover:bg-rose-500/10 font-semibold flex items-center gap-1.5 transition-colors disabled:opacity-50 text-[13px]"
                  >
                    {t('sendEmail.discardDraft')}
                  </button>
                )}
                {isSaving ? (
                  <span className="text-[11.5px] text-slate-400 dark:text-slate-500 flex items-center gap-1">
                    <Loader2 className="w-3 h-3 animate-spin" /> {t('sendEmail.savingDraft')}
                  </span>
                ) : lastSavedState ? (
                  <span className="text-[11.5px] text-slate-400 dark:text-slate-500">
                    {t('sendEmail.draftSaved')}
                  </span>
                ) : null}
              </div>
              <button
                onClick={sendAction}
                disabled={replyMutation.isPending || (replyMode === 'forward' && to.length === 0)}
                className="h-10 px-6 rounded-lg bg-brand-600 text-white font-semibold flex items-center gap-2 hover:bg-brand-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed text-[13.5px]"
              >
                {replyMutation.isPending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
                Send
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
