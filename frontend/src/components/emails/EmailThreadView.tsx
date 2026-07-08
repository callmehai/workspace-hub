import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Paperclip, Download, ChevronDown, ChevronRight, Reply, ReplyAll, Forward, Loader2, Send, X } from 'lucide-react';
import { sendEmailApi, fileToAttachmentUpload, MAX_ATTACHMENT_TOTAL_BYTES, type EmailAttachmentDto } from '../../lib/sendEmailApi';
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

export const EmailThreadView: React.FC<EmailThreadViewProps> = ({ itemId, connectionId }) => {
  const { t, lang } = useI18n();
  const dl = lang === 'vi' ? 'vi-VN' : 'en-US';
  const queryClient = useQueryClient();

  // undefined = chưa bấm → mặc định (thư mới nhất mở, còn lại thu gọn); true/false = user đã toggle.
  const [expandedMsgs, setExpandedMsgs] = useState<Record<string, boolean | undefined>>({});
  const [replyMode, setReplyMode] = useState<'reply' | 'replyAll' | 'forward' | null>(null);

  const [to, setTo] = useState<string[]>([]);
  const [cc, setCc] = useState<string[]>([]);
  const [bcc, setBcc] = useState<string[]>([]);
  const [bodyHtml, setBodyHtml] = useState<string>('');
  const [includeAttachments, setIncludeAttachments] = useState(true);
  const [attachFiles, setAttachFiles] = useState<File[]>([]);

  const { data: thread, isLoading, isError } = useQuery({
    queryKey: ['emailThread', itemId],
    queryFn: () => sendEmailApi.getThread(itemId),
  });

  const toggleMsg = (msgId: string, currentlyExpanded: boolean) => {
    setExpandedMsgs(prev => ({ ...prev, [msgId]: !currentlyExpanded }));
  };

  const handleAction = (mode: 'reply' | 'replyAll' | 'forward') => {
    setReplyMode(mode);
    setBodyHtml('');
    setTo([]);
    setCc([]);
    setBcc([]);
    setIncludeAttachments(true);
    setAttachFiles([]);

    // Cuộn xuống box
    setTimeout(() => {
      document.getElementById('reply-box')?.scrollIntoView({ behavior: 'smooth' });
    }, 100);
  };

  const replyMutation = useMutation({
    mutationFn: async (mode: 'reply' | 'replyAll' | 'forward') => {
      const attachments = attachFiles.length > 0
        ? await Promise.all(attachFiles.map(fileToAttachmentUpload))
        : undefined;

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
    },
    onSuccess: () => {
      toast.success(t('item.saved') || 'Sent successfully');
      setReplyMode(null);
      setBodyHtml('');
      setTo([]);
      setCc([]);
      setBcc([]);
      setAttachFiles([]);
      queryClient.invalidateQueries({ queryKey: ['emailThread', itemId] });
    },
    onError: (err) => {
      toast.error(t('item.saveFail') || 'Failed to send email');
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

  const downloadAttachment = async (_msgId: string, att: EmailAttachmentDto) => {
    try {
      toast.loading(t('common.loading') || 'Downloading...', { id: `dl-${att.attachmentId}` });
      await sendEmailApi.downloadAttachment(itemId, att.attachmentId, att.filename);
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

  return (
    <div className="space-y-4">
      <div className="text-[11px] font-semibold tracking-[0.04em] uppercase text-slate-400 dark:text-slate-500 mb-2">
        {t('sendEmail.content') || 'Hội thoại'} ({thread.messages.length})
      </div>
      
      <div className="space-y-3">
        {thread.messages.map((msg, index) => {
          const isLatest = index === thread.messages.length - 1;
          // Mặc định: thư mới nhất mở, thư cũ thu gọn — cho tới khi user tự toggle.
          const isExpanded = expandedMsgs[msg.messageId] ?? isLatest;
          const fromName = msg.from ? msg.from.split('<')[0].trim() : 'Unknown';

          return (
            <div key={msg.messageId} className="border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden bg-white dark:bg-slate-900 shadow-sm transition-all">
              {/* Header của từng message */}
              <button
                onClick={() => toggleMsg(msg.messageId, isExpanded)}
                className="w-full flex items-center justify-between px-4 py-3 bg-slate-50 hover:bg-slate-100 dark:bg-slate-800 dark:hover:bg-slate-700/80 transition-colors"
              >
                <div className="flex items-center gap-3 overflow-hidden">
                  <div className="w-8 h-8 rounded-full bg-brand-100 text-brand-600 dark:bg-brand-500/20 dark:text-brand-400 flex items-center justify-center font-bold text-sm shrink-0">
                    {fromName.charAt(0).toUpperCase()}
                  </div>
                  <div className="flex flex-col items-start truncate">
                    <span className="text-sm font-semibold text-slate-800 dark:text-slate-100 truncate">{fromName}</span>
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

                  {/* HTML Body */}
                  <div 
                    className="text-[13.5px] text-slate-900 dark:text-slate-100 leading-[1.65] break-words overflow-x-auto email-body-content"
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
            <button onClick={() => setReplyMode(null)} className="p-1 rounded hover:bg-brand-200/50 dark:hover:bg-brand-500/20 text-brand-700 dark:text-brand-400 transition-colors">
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
            
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-semibold text-slate-500 mb-1">Cc</label>
                <EmailChipsInput value={cc} onChange={setCc} placeholder="Add Cc..." connectionId={connectionId} />
              </div>
              <div>
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

            <div className="flex justify-end pt-2">
              <button
                onClick={sendAction}
                disabled={replyMutation.isPending || (replyMode === 'forward' && to.length === 0)}
                className="h-10 px-6 rounded-lg bg-brand-600 text-white font-semibold flex items-center gap-2 hover:bg-brand-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
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
