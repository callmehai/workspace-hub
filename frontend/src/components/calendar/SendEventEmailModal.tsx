import React, { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { X, Send, Mail, UserPlus, Loader2 } from 'lucide-react';
import toast from 'react-hot-toast';
import { itemsApi } from '../../lib/itemsApi';
import { useI18n } from '../../hooks/useI18n';
import { handleApiError } from '../../lib/errorUtils';

interface SendEventEmailModalProps {
  itemId: string;
  eventTitle: string;
  eventDescription: string;
  eventLocation: string;
  eventTime: string;
  eventMeetUrl: string;
  guests: { email: string; displayName?: string | null }[];
  onClose: () => void;
}

export const SendEventEmailModal: React.FC<SendEventEmailModalProps> = ({
  itemId,
  eventTitle,
  eventDescription,
  eventLocation,
  eventTime,
  eventMeetUrl,
  guests,
  onClose
}) => {
  const { t } = useI18n();

  const [recipients, setRecipients] = useState<string[]>(() => 
    guests.map(g => g.email).filter(Boolean)
  );
  const [recipientInput, setRecipientInput] = useState('');
  const [subject, setSubject] = useState(`Thư mời tham dự: ${eventTitle}`);
  const [bodyText, setBodyText] = useState(() => {
    return `Xin chào,\n\nBạn được mời tham dự sự kiện "${eventTitle}".\n\nThông tin chi tiết sự kiện:\n- Thời gian: ${eventTime}\n${eventLocation ? `- Địa điểm: ${eventLocation}\n` : ''}${eventMeetUrl ? `- Google Meet: ${eventMeetUrl}\n` : ''}\n${eventDescription ? `Nội dung mô tả:\n${eventDescription}\n\n` : ''}Trân trọng,\nWorkspace Hub`;
  });
  const [sendCopyToMe, setSendCopyToMe] = useState(true);

  const characterCount = bodyText.length;
  const maxCharacters = 2400;

  // Send Email Mutation
  const sendMutation = useMutation({
    mutationFn: () => {
      // Map bodyText lines to HTML paragraph tags or clean <pre> formatted text
      const bodyHtml = `<div style="font-family: sans-serif; padding: 20px; border: 1px solid #e2e8f0; border-radius: 8px; max-width: 600px; margin: 0 auto; color: #1e293b;">
        <h2 style="color: #4f46e5; margin-top: 0;">Thư mời tham dự sự kiện</h2>
        <div style="background-color: #f8fafc; padding: 15px; border-radius: 8px; border: 1px solid #f1f5f9; margin-bottom: 20px;">
          <h3 style="margin-top: 0; color: #0f172a; font-size: 16px;">${eventTitle}</h3>
          <p style="margin: 4px 0; font-size: 13px;"><strong>Thời gian:</strong> ${eventTime}</p>
          ${eventLocation ? `<p style="margin: 4px 0; font-size: 13px;"><strong>Địa điểm:</strong> ${eventLocation}</p>` : ''}
          ${eventMeetUrl ? `<p style="margin: 4px 0; font-size: 13px;"><strong>Google Meet:</strong> <a href="${eventMeetUrl}" style="color: #4f46e5; text-decoration: underline;">Tham gia cuộc họp</a></p>` : ''}
        </div>
        <div style="white-space: pre-wrap; font-size: 14px; line-height: 1.6; color: #334155;">
          ${bodyText.replace(/\n/g, '<br/>')}
        </div>
        <hr style="border: 0; border-top: 1px solid #e2e8f0; margin: 25px 0 15px 0;" />
        <p style="font-size: 11px; color: #94a3b8; text-align: center; margin: 0;">Email này được gửi tự động từ Workspace Hub.</p>
      </div>`;

      return itemsApi.sendEmailToGuests(itemId, {
        recipientEmails: recipients,
        subject,
        bodyHtml,
        sendCopyToMe
      });
    },
    onSuccess: () => {
      toast.success(t('calendar.emailSentSuccess'));
      onClose();
    },
    onError: (err) => handleApiError(err, 'Lỗi gửi email')
  });

  const handleAddRecipient = (e: React.FormEvent) => {
    e.preventDefault();
    const email = recipientInput.trim().toLowerCase();
    if (!email) return;
    
    // Simple email regex validation
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    if (!emailRegex.test(email)) {
      toast.error(t('calendar.invalidEmail'));
      return;
    }

    if (recipients.includes(email)) {
      toast.error(t('calendar.emailExists'));
      return;
    }

    setRecipients([...recipients, email]);
    setRecipientInput('');
  };

  const handleRemoveRecipient = (email: string) => {
    setRecipients(recipients.filter(r => r !== email));
  };

  const handleSend = () => {
    if (recipients.length === 0) {
      toast.error(t('calendar.noRecipients'));
      return;
    }
    if (!subject.trim()) {
      toast.error(t('calendar.subjectEmpty'));
      return;
    }
    if (!bodyText.trim()) {
      toast.error(t('calendar.bodyEmpty'));
      return;
    }
    if (characterCount > maxCharacters) {
      toast.error(t('calendar.bodyTooLong'));
      return;
    }

    sendMutation.mutate();
  };

  return (
    <div className="fixed inset-0 z-[9000] flex items-center justify-center bg-slate-900/40 backdrop-blur-[2.5px] p-4" onClick={onClose}>
      <div 
        className="w-full max-w-lg rounded-2xl border border-slate-200/80 bg-white shadow-2xl dark:border-slate-800 dark:bg-slate-950 flex flex-col max-h-[90vh] animate-in fade-in zoom-in-95 duration-150 overflow-hidden"
        onClick={event => event.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-200/60 dark:border-slate-800 shrink-0">
          <div className="flex items-center gap-2">
            <Mail className="w-5 h-5 text-indigo-500" />
            <h3 className="text-[16px] font-bold text-slate-900 dark:text-slate-50">{t('calendar.sendEmailGuestsTitle')}</h3>
          </div>
          <button 
            onClick={onClose} 
            className="p-1.5 text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 rounded-lg transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content (Scrollable) */}
        <div className="flex-1 overflow-y-auto p-5 space-y-4">
          
          {/* Add Recipients Form */}
          <div className="space-y-1.5">
            <label className="text-xs font-bold text-slate-500 dark:text-slate-400 uppercase tracking-wider">{t('calendar.recipientsLabel')}</label>
            <form onSubmit={handleAddRecipient} className="flex gap-2">
              <div className="relative flex-1">
                <input
                  type="text"
                  value={recipientInput}
                  onChange={e => setRecipientInput(e.target.value)}
                  placeholder={t('calendar.addEmailPlaceholder')}
                  className="w-full bg-slate-50 dark:bg-slate-900/50 border border-slate-200 dark:border-slate-800 rounded-lg pl-3 pr-8 py-2 text-sm font-medium focus:outline-none focus:ring-1 focus:ring-indigo-500/50 focus:border-indigo-400"
                />
                <button
                  type="submit"
                  className="absolute right-2 top-1.5 p-1 text-slate-400 hover:text-indigo-600 dark:hover:text-indigo-400 rounded-md transition-colors"
                >
                  <UserPlus className="w-4.5 h-4.5" />
                </button>
              </div>
            </form>

            {/* Recipients list chips */}
            <div className="flex flex-wrap gap-1.5 max-h-24 overflow-y-auto pt-1">
              {recipients.length === 0 ? (
                <span className="text-xs text-slate-400 dark:text-slate-500 italic">{t('calendar.noRecipientsYet')}</span>
              ) : (
                recipients.map((email, idx) => (
                  <span 
                    key={idx} 
                    className="inline-flex items-center gap-1 pl-2.5 pr-1 py-1 rounded-full bg-indigo-50 dark:bg-indigo-950/20 text-indigo-700 dark:text-indigo-400 text-xs font-semibold border border-indigo-100/50 dark:border-indigo-900/30"
                  >
                    <span>{email}</span>
                    <button 
                      type="button"
                      onClick={() => handleRemoveRecipient(email)}
                      className="p-0.5 hover:bg-indigo-100 dark:hover:bg-indigo-900/60 rounded-full text-indigo-500 transition-colors"
                    >
                      <X className="w-3 h-3" />
                    </button>
                  </span>
                ))
              )}
            </div>
          </div>

          {/* Subject Line */}
          <div className="space-y-1.5">
            <label className="text-xs font-bold text-slate-500 dark:text-slate-400 uppercase tracking-wider">{t('calendar.emailSubjectLabel')}</label>
            <input
              type="text"
              value={subject}
              onChange={e => setSubject(e.target.value)}
              placeholder={t('calendar.emailSubjectPlaceholder')}
              className="w-full bg-slate-50 dark:bg-slate-900/50 border border-slate-200 dark:border-slate-800 rounded-lg px-3 py-2 text-sm font-semibold focus:outline-none focus:ring-1 focus:ring-indigo-500/50 focus:border-indigo-400"
            />
          </div>

          {/* Email Body textarea */}
          <div className="space-y-1.5">
            <div className="flex justify-between items-center">
              <label className="text-xs font-bold text-slate-500 dark:text-slate-400 uppercase tracking-wider">{t('calendar.emailBodyLabel')}</label>
              <span className={`text-[10px] font-semibold ${characterCount > maxCharacters ? 'text-rose-500' : 'text-slate-400'}`}>
                {characterCount} / {maxCharacters}
              </span>
            </div>
            <textarea
              value={bodyText}
              onChange={e => setBodyText(e.target.value)}
              rows={8}
              placeholder={t('calendar.emailBodyPlaceholder')}
              className="w-full bg-slate-50 dark:bg-slate-900/50 border border-slate-200 dark:border-slate-800 rounded-lg p-3 text-xs font-medium leading-relaxed focus:outline-none focus:ring-1 focus:ring-indigo-500/50 focus:border-indigo-400 resize-none"
            />
          </div>

          {/* Send Copy to Me Checkbox */}
          <label className="flex items-center gap-2.5 cursor-pointer py-1 selection:bg-transparent">
            <input
              type="checkbox"
              checked={sendCopyToMe}
              onChange={e => setSendCopyToMe(e.target.checked)}
              className="w-4 h-4 rounded border-slate-350 text-indigo-600 focus:ring-indigo-500/40 focus:ring-offset-0 shrink-0"
            />
            <span className="text-xs font-bold text-slate-600 dark:text-slate-300">{t('calendar.sendCopyOption')}</span>
          </label>
        </div>

        {/* Footer actions */}
        <div className="px-5 py-3.5 border-t border-slate-250/60 dark:border-slate-800 bg-slate-50 dark:bg-slate-900 flex justify-end gap-2 shrink-0">
          <button
            onClick={onClose}
            className="h-9 px-4 rounded-lg text-xs font-bold bg-white border border-slate-200 dark:bg-slate-800 dark:border-slate-700 text-slate-700 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700 transition-colors"
          >
            {t('common.cancel')}
          </button>
          <button
            onClick={handleSend}
            disabled={sendMutation.isPending}
            className="h-9 px-4 rounded-lg text-xs font-bold bg-indigo-600 hover:bg-indigo-700 text-white flex items-center gap-1.5 shadow-sm transition-colors"
          >
            {sendMutation.isPending ? (
              <Loader2 className="w-3.5 h-3.5 animate-spin" />
            ) : (
              <Send className="w-3.5 h-3.5" />
            )}
            <span>{t('calendar.sendEmailBtn')}</span>
          </button>
        </div>
      </div>
    </div>
  );
};
