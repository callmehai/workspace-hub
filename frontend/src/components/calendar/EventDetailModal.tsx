import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { 
  X, Pencil, Trash2, Mail, MoreVertical, Link, MapPin, 
  Users, Paperclip, Bell, Check, HelpCircle, AlertCircle, 
  MessageSquare, Loader2, Calendar, ExternalLink
} from 'lucide-react';
import toast from 'react-hot-toast';
import { itemsApi } from '../../lib/itemsApi';
import { useI18n } from '../../hooks/useI18n';
import { handleApiError } from '../../lib/errorUtils';
import { SendEventEmailModal } from './SendEventEmailModal';

interface EventDetailModalProps {
  itemId: string;
  onClose: () => void;
  onEdit: (detail: any) => void;
  onDelete: () => void;
}

export const EventDetailModal: React.FC<EventDetailModalProps> = ({
  itemId,
  onClose,
  onEdit,
  onDelete
}) => {
  const { t, lang } = useI18n();
  const dl = lang === 'vi' ? 'vi-VN' : 'en-US';
  const queryClient = useQueryClient();

  const [isEmailModalOpen, setIsEmailModalOpen] = useState(false);
  const [isNoteInputOpen, setIsNoteInputOpen] = useState(false);
  const [rsvpNote, setRsvpNote] = useState('');
  const [showMoreActions, setShowMoreActions] = useState(false);

  // Fetch GCal details
  const { data: detail, isLoading, isError, refetch } = useQuery({
    queryKey: ['calendar-event-detail', itemId],
    queryFn: () => itemsApi.getCalendarEventDetail(itemId),
    enabled: !!itemId,
  });

  // RSVP Mutation
  const rsvpMutation = useMutation({
    mutationFn: ({ status, comment }: { status: string; comment?: string }) => 
      itemsApi.rsvpEvent(itemId, status, comment),
    onSuccess: () => {
      toast.success(t('calendar.rsvpSuccess'));
      queryClient.invalidateQueries({ queryKey: ['calendar-event-detail', itemId] });
      queryClient.invalidateQueries({ queryKey: ['calendar-items'] });
      setIsNoteInputOpen(false);
    },
    onError: (err) => handleApiError(err, 'Lỗi cập nhật RSVP')
  });

  if (isLoading) {
    return (
      <div className="fixed inset-0 z-[8000] flex items-center justify-center bg-slate-900/30 backdrop-blur-[2px]">
        <div className="w-full max-w-md rounded-2xl border border-slate-200 bg-white p-6 shadow-2xl dark:border-slate-800 dark:bg-slate-900 flex flex-col items-center justify-center space-y-4">
          <Loader2 className="w-8 h-8 animate-spin text-amber-500" />
          <span className="text-sm font-medium text-slate-500 dark:text-slate-400">{t('calendar.loadingDetail')}</span>
        </div>
      </div>
    );
  }

  if (isError || !detail) {
    return (
      <div className="fixed inset-0 z-[8000] flex items-center justify-center bg-slate-900/30 backdrop-blur-[2px]">
        <div className="w-full max-w-md rounded-2xl border border-slate-200 bg-white p-6 shadow-2xl dark:border-slate-800 dark:bg-slate-900 text-center flex flex-col items-center">
          <AlertCircle className="w-12 h-12 text-rose-500 mb-3" />
          <h3 className="text-base font-semibold text-slate-800 dark:text-slate-100 mb-1">{t('item.loadError')}</h3>
          <p className="text-xs text-slate-400 dark:text-slate-500 mb-4">{t('item.loadErrorHint')}</p>
          <div className="flex gap-2.5">
            <button onClick={() => refetch()} className="px-4 py-2 bg-brand-600 text-white rounded-lg text-sm font-medium hover:bg-brand-700 transition-colors">
              {t('item.reload')}
            </button>
            <button onClick={onClose} className="px-4 py-2 bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 rounded-lg text-sm font-medium hover:bg-slate-200 dark:hover:bg-slate-700 transition-colors">
              {t('common.close')}
            </button>
          </div>
        </div>
      </div>
    );
  }

  // Format Date Range
  const start = detail.start ? new Date(detail.start) : null;
  const end = detail.end ? new Date(detail.end) : null;
  let dateString = '';
  if (start) {
    const startStr = start.toLocaleString(dl, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
    if (detail.allDay) {
      dateString = `${startStr} (${t('calendar.allDay')})`;
    } else if (end) {
      const timeStart = start.toLocaleTimeString(dl, { hour: '2-digit', minute: '2-digit' });
      const timeEnd = end.toLocaleTimeString(dl, { hour: '2-digit', minute: '2-digit' });
      const isSameDay = start.toDateString() === end.toDateString();
      if (isSameDay) {
        dateString = `${startStr}, ${timeStart} – ${timeEnd}`;
      } else {
        const endStr = end.toLocaleString(dl, { day: 'numeric', month: 'long', year: 'numeric' });
        dateString = `${startStr} lúc ${timeStart} – ${endStr} lúc ${timeEnd}`;
      }
    } else {
      dateString = startStr;
    }
  }

  // Copy GCal or app share link
  const handleCopyLink = () => {
    const link = detail.htmlLink || `${window.location.origin}/calendar?eventId=${itemId}`;
    navigator.clipboard.writeText(link);
    toast.success(t('calendar.copiedShareLink'));
  };

  // Find user's current RSVP
  const currentUserEmail = detail.owningCalendarName;
  const userRsvp = detail.attendees?.find((a: any) => a.email.toLowerCase() === currentUserEmail?.toLowerCase());
  const currentResponse = userRsvp?.responseStatus || 'needsAction';

  // Count accepted
  const guestCount = detail.attendees?.length ?? 0;
  const acceptedCount = detail.attendees?.filter((a: any) => a.responseStatus === 'accepted').length ?? 0;

  const handleRsvpSubmit = (status: string) => {
    rsvpMutation.mutate({ status, comment: rsvpNote.trim() || undefined });
  };

  return (
    <>
      <div className="fixed inset-0 z-[8000] flex items-center justify-center bg-slate-900/35 backdrop-blur-[2.5px] p-4" onClick={onClose}>
        <div 
          className="w-full max-w-lg rounded-2xl border border-slate-200/80 bg-white shadow-2xl dark:border-slate-800 dark:bg-slate-950 flex flex-col max-h-[90vh] animate-in fade-in zoom-in-95 duration-150 overflow-hidden" 
          onClick={event => event.stopPropagation()}
        >
          {/* Top Actions Bar */}
          <div className="flex items-center justify-end gap-1 px-4 py-3 bg-slate-50 dark:bg-slate-900/60 border-b border-slate-200/60 dark:border-slate-800/80 shrink-0">
            <button 
              onClick={() => onEdit(detail)} 
              title={t('common.edit')} 
              className="p-1.5 text-slate-500 hover:text-slate-880 hover:bg-slate-200/60 dark:text-slate-400 dark:hover:text-slate-100 dark:hover:bg-slate-800 rounded-lg transition-colors"
            >
              <Pencil className="w-[18px] h-[18px]" />
            </button>
            <button 
              onClick={onDelete} 
              title={t('common.delete')} 
              className="p-1.5 text-slate-500 hover:text-rose-600 hover:bg-rose-50 dark:text-slate-400 dark:hover:text-rose-400 dark:hover:bg-rose-950/30 rounded-lg transition-colors"
            >
              <Trash2 className="w-[18px] h-[18px]" />
            </button>
            <button 
              onClick={() => setIsEmailModalOpen(true)} 
              title={t('calendar.emailGuests')} 
              className="p-1.5 text-slate-500 hover:text-slate-800 hover:bg-slate-200/60 dark:text-slate-400 dark:hover:text-slate-100 dark:hover:bg-slate-800 rounded-lg transition-colors"
            >
              <Mail className="w-[18px] h-[18px]" />
            </button>
            <div className="relative">
              <button 
                onClick={() => setShowMoreActions(!showMoreActions)} 
                className="p-1.5 text-slate-500 hover:text-slate-800 hover:bg-slate-200/60 dark:text-slate-400 dark:hover:text-slate-100 dark:hover:bg-slate-800 rounded-lg transition-colors"
              >
                <MoreVertical className="w-[18px] h-[18px]" />
              </button>
              {showMoreActions && (
                <div className="absolute right-0 mt-1 w-44 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 shadow-xl rounded-lg py-1 z-10 animate-in fade-in duration-100">
                  <button 
                    onClick={() => { handleCopyLink(); setShowMoreActions(false); }}
                    className="w-full text-left px-3.5 py-2 text-xs font-semibold text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700 flex items-center gap-2"
                  >
                    <Link className="w-3.5 h-3.5 text-slate-400" />
                    <span>{t('calendar.copyLink')}</span>
                  </button>
                  {detail.htmlLink && (
                    <a 
                      href={detail.htmlLink}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="w-full text-left px-3.5 py-2 text-xs font-semibold text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700 flex items-center gap-2"
                      onClick={() => setShowMoreActions(false)}
                    >
                      <ExternalLink className="w-3.5 h-3.5 text-slate-400" />
                      <span>{t('calendar.viewGoogle')}</span>
                    </a>
                  )}
                </div>
              )}
            </div>
            <div className="w-[1px] h-5 bg-slate-200 dark:bg-slate-800 mx-1" />
            <button 
              onClick={onClose} 
              className="p-1.5 text-slate-400 dark:text-slate-500 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 rounded-lg transition-colors"
            >
              <X className="w-5 h-5" />
            </button>
          </div>

          {/* Main Info Body (Scrollable) */}
          <div className="flex-1 overflow-y-auto p-6 space-y-6">
            {/* Title & Time */}
            <div>
              <div className="flex items-center gap-2 mb-1.5">
                <span className="w-3 h-3 rounded bg-amber-500 block shrink-0" />
                <span className="text-[12.5px] font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wider">{t('calendar.event')}</span>
              </div>
              <h2 className="text-[20px] font-bold text-slate-900 dark:text-slate-50 leading-snug">{detail.title}</h2>
              <p className="mt-2 text-sm font-semibold text-slate-600 dark:text-slate-300 flex items-center gap-2">
                <Calendar className="w-4.5 h-4.5 text-slate-400 shrink-0" />
                <span>{dateString}</span>
              </p>
            </div>

            {/* Invite via Link Pill Button */}
            <div>
              <button
                onClick={handleCopyLink}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full border border-slate-200 dark:border-slate-800 bg-slate-50/50 hover:bg-slate-100 dark:bg-slate-900/40 dark:hover:bg-slate-800/80 text-xs font-semibold text-slate-700 dark:text-slate-300 shadow-sm transition-all"
              >
                <Link className="w-3.5 h-3.5 text-indigo-500" />
                <span>{t('calendar.inviteViaLink')}</span>
              </button>
            </div>

            {/* Location Row */}
            {detail.location && (
              <div className="flex gap-3 text-sm">
                <MapPin className="w-4.5 h-4.5 text-slate-400 shrink-0 mt-0.5" />
                <div className="space-y-0.5">
                  <span className="text-xs text-slate-400 dark:text-slate-500 block">{t('item.location')}</span>
                  <span className="font-medium text-slate-800 dark:text-slate-200">{detail.location}</span>
                </div>
              </div>
            )}

            {/* Meet URL Option */}
            {detail.meetUrl && (
              <div className="flex gap-3 text-sm">
                <div className="w-4.5 h-4.5 flex items-center justify-center shrink-0 mt-0.5">
                  <span className="w-2.5 h-2.5 rounded-full bg-emerald-500 animate-pulse" />
                </div>
                <div className="space-y-1">
                  <span className="text-xs text-slate-400 dark:text-slate-500 block">{t('calendar.googleMeet')}</span>
                  <a 
                    href={detail.meetUrl} 
                    target="_blank" 
                    rel="noopener noreferrer"
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-indigo-600 hover:bg-indigo-700 text-white rounded-lg text-xs font-bold shadow transition-colors"
                  >
                    <span>{t('calendar.joinMeet')}</span>
                    <ExternalLink className="w-3.5 h-3.5" />
                  </a>
                </div>
              </div>
            )}

            {/* Guests list */}
            <div>
              <div className="flex gap-3 items-start mb-2.5">
                <Users className="w-4.5 h-4.5 text-slate-400 mt-0.5" />
                <div className="text-sm">
                  <span className="text-xs text-slate-400 dark:text-slate-500 block">{t('calendar.guests')}</span>
                  <span className="font-semibold text-slate-800 dark:text-slate-200">
                    {guestCount} {t('calendar.guestCount')} · {acceptedCount} {t('calendar.acceptedCount')}
                  </span>
                </div>
              </div>

              {guestCount > 0 && (
                <div className="ml-7 border border-slate-100 dark:border-slate-800/80 rounded-xl bg-slate-50/40 dark:bg-slate-900/10 divide-y divide-slate-100 dark:divide-slate-850 p-1.5 max-h-48 overflow-y-auto space-y-1">
                  {detail.attendees.map((attendee: any, idx: number) => {
                    const isUser = attendee.email.toLowerCase() === currentUserEmail?.toLowerCase();
                    let responseColor = 'bg-slate-300';
                    let responseIcon = null;
                    if (attendee.responseStatus === 'accepted') {
                      responseColor = 'bg-emerald-500';
                      responseIcon = <Check className="w-3 h-3 text-white" />;
                    } else if (attendee.responseStatus === 'declined') {
                      responseColor = 'bg-rose-500';
                      responseIcon = <X className="w-3 h-3 text-white" />;
                    } else if (attendee.responseStatus === 'tentative') {
                      responseColor = 'bg-amber-500';
                      responseIcon = <HelpCircle className="w-3 h-3 text-white" />;
                    }

                    return (
                      <div key={idx} className="flex items-center justify-between py-1.5 px-2.5 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-800/50">
                        <div className="min-w-0 flex-1 pr-3">
                          <div className="flex items-center gap-1.5">
                            <span className="text-xs font-semibold text-slate-800 dark:text-slate-200 truncate">
                              {attendee.displayName || attendee.email.split('@')[0]}
                            </span>
                            {attendee.organizer && (
                              <span className="px-1.5 py-0.2 bg-indigo-50 text-indigo-600 dark:bg-indigo-500/10 dark:text-indigo-400 border border-indigo-100 dark:border-indigo-500/20 rounded text-[9.5px] font-bold">
                                {t('calendar.organizer')}
                              </span>
                            )}
                          </div>
                          <span className="text-[11px] text-slate-400 dark:text-slate-500 block truncate">{attendee.email} {isUser && `(${t('calendar.you')})`}</span>
                        </div>

                        {/* RSVP response badge */}
                        <div className="shrink-0 flex items-center gap-1.5">
                          {attendee.comment && (
                            <span className="p-1 bg-slate-100 dark:bg-slate-800 text-slate-400 dark:text-slate-500 rounded" title={attendee.comment}>
                              <MessageSquare className="w-3.5 h-3.5" />
                            </span>
                          )}
                          <div className={`w-5 h-5 rounded-full flex items-center justify-center ${responseColor}`}>
                            {responseIcon ?? <span className="w-1.5 h-1.5 rounded-full bg-slate-400" />}
                          </div>
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>

            {/* Description content */}
            {detail.description && (
              <div className="space-y-1.5">
                <span className="text-xs font-semibold tracking-wider text-slate-400 dark:text-slate-500 uppercase block">{t('sendEmail.content')}</span>
                <div className="text-[13px] text-slate-700 dark:text-slate-300 leading-relaxed bg-slate-50 dark:bg-slate-900/40 rounded-xl p-3.5 border border-slate-100 dark:border-slate-800/60 whitespace-pre-wrap">
                  {detail.description}
                </div>
              </div>
            )}

            {/* Attached files (Drive Attachments) */}
            {detail.driveAttachments && detail.driveAttachments.length > 0 && (
              <div className="space-y-2">
                <span className="text-xs font-semibold tracking-wider text-slate-400 dark:text-slate-500 uppercase block flex items-center gap-1">
                  <Paperclip className="w-3.5 h-3.5" />
                  <span>{t('calendar.attachments')} ({detail.driveAttachments.length})</span>
                </span>
                <div className="flex flex-wrap gap-2">
                  {detail.driveAttachments.map((attachment: any, idx: number) => (
                    <a
                      key={idx}
                      href={attachment.fileUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="inline-flex items-center gap-2 px-3 py-2 rounded-xl bg-indigo-50/40 hover:bg-indigo-50 border border-indigo-100/50 hover:border-indigo-200 text-xs text-indigo-700 dark:bg-slate-900/60 dark:hover:bg-slate-800 dark:border-slate-800 dark:text-indigo-400 font-semibold shadow-sm transition-all"
                    >
                      <Paperclip className="w-3.5 h-3.5" />
                      <span className="truncate max-w-[150px]">{attachment.title || 'Untitled file'}</span>
                    </a>
                  ))}
                </div>
              </div>
            )}

            {/* Reminders summary */}
            <div>
              <div className="flex gap-3 items-center">
                <Bell className="w-4.5 h-4.5 text-slate-400 shrink-0" />
                <div className="text-sm">
                  <span className="text-xs text-slate-400 dark:text-slate-500 block">{t('calendar.reminders')}</span>
                  <div className="flex flex-col gap-1 mt-0.5">
                    {detail.reminders && detail.reminders.length > 0 ? (
                      detail.reminders.map((r: any, idx: number) => {
                        const typeLabel = r.reminderType === 'Notification' 
                          ? t('calendar.inApp') 
                          : r.reminderType === 'Email' 
                            ? 'Email' 
                            : t('calendar.both');
                        const unitLabel = r.offsetUnit.toLowerCase() === 'minutes' 
                          ? t('calendar.minutes') 
                          : r.offsetUnit.toLowerCase() === 'hours' 
                            ? t('calendar.hours') 
                            : r.offsetUnit.toLowerCase() === 'days' 
                              ? t('calendar.days') 
                              : t('calendar.weeks');
                        const timeStr = r.timeOfDay ? ` lúc ${r.timeOfDay}` : '';
                        return (
                          <span key={idx} className="font-medium text-slate-700 dark:text-slate-350">
                            • {typeLabel}: {r.offsetValue} {unitLabel}{timeStr} {t('calendar.before')}
                          </span>
                        );
                      })
                    ) : (
                      <span className="text-slate-400 dark:text-slate-500 italic">{t('calendar.noReminders')}</span>
                    )}
                  </div>
                </div>
              </div>
            </div>

            {/* Owning Calendar account */}
            <div className="text-[11px] text-slate-400 dark:text-slate-500 italic pt-2 border-t border-slate-100 dark:border-slate-800/80">
              {t('calendar.ownedBy')} {detail.owningCalendarName}
            </div>
          </div>

          {/* RSVP Bar (Fixed at bottom) */}
          <div className="px-6 py-4 bg-slate-50 dark:bg-slate-900 border-t border-slate-200/85 dark:border-slate-800 shrink-0 space-y-3">
            <div className="flex items-center justify-between gap-4">
              <span className="text-xs font-bold text-slate-500 dark:text-slate-400 uppercase tracking-wider">{t('calendar.joinQuestion')}</span>
              
              {/* RSVP Actions buttons */}
              <div className="flex gap-2">
                <button
                  onClick={() => handleRsvpSubmit('accepted')}
                  disabled={rsvpMutation.isPending}
                  className={`h-9 px-4 rounded-lg text-xs font-bold transition-all shadow-sm ${
                    currentResponse === 'accepted'
                      ? 'bg-emerald-600 hover:bg-emerald-700 text-white'
                      : 'bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700'
                  }`}
                >
                  {t('calendar.rsvpYes')}
                </button>
                <button
                  onClick={() => handleRsvpSubmit('declined')}
                  disabled={rsvpMutation.isPending}
                  className={`h-9 px-4 rounded-lg text-xs font-bold transition-all shadow-sm ${
                    currentResponse === 'declined'
                      ? 'bg-rose-600 hover:bg-rose-700 text-white'
                      : 'bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700'
                  }`}
                >
                  {t('calendar.rsvpNo')}
                </button>
                <button
                  onClick={() => handleRsvpSubmit('tentative')}
                  disabled={rsvpMutation.isPending}
                  className={`h-9 px-4 rounded-lg text-xs font-bold transition-all shadow-sm ${
                    currentResponse === 'tentative'
                      ? 'bg-amber-600 hover:bg-amber-700 text-white'
                      : 'bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700'
                  }`}
                >
                  {t('calendar.rsvpMaybe')}
                </button>
                <button
                  type="button"
                  onClick={() => setIsNoteInputOpen(!isNoteInputOpen)}
                  className={`h-9 px-3 rounded-lg text-xs font-semibold border flex items-center justify-center gap-1.5 transition-colors ${
                    isNoteInputOpen || rsvpNote.trim()
                      ? 'bg-indigo-50 border-indigo-200 text-indigo-600 dark:bg-indigo-950/20 dark:border-indigo-900/50 dark:text-indigo-400'
                      : 'bg-white border-slate-200 hover:bg-slate-50 text-slate-500 dark:bg-slate-800 dark:border-slate-700 dark:text-slate-400'
                  }`}
                >
                  <MessageSquare className="w-3.5 h-3.5" />
                  <span>{t('calendar.addNote')}</span>
                </button>
              </div>
            </div>

            {/* Note text field */}
            {isNoteInputOpen && (
              <div className="flex gap-2 items-center animate-in slide-in-from-bottom-2 duration-150">
                <input
                  type="text"
                  value={rsvpNote}
                  onChange={e => setRsvpNote(e.target.value)}
                  placeholder={t('calendar.rsvpNotePlaceholder')}
                  className="flex-1 bg-white border border-slate-200 dark:bg-slate-850 dark:border-slate-700/80 rounded-lg px-3 py-1.5 text-xs font-medium focus:outline-none focus:ring-1 focus:ring-indigo-500/50"
                  maxLength={100}
                />
                <button 
                  onClick={() => handleRsvpSubmit(currentResponse)}
                  disabled={rsvpMutation.isPending}
                  className="h-[30px] px-3 bg-brand-600 text-white rounded-lg text-xs font-bold hover:bg-brand-700 transition-colors"
                >
                  {t('common.save')}
                </button>
              </div>
            )}
          </div>
        </div>
      </div>

      {isEmailModalOpen && (
        <SendEventEmailModal
          itemId={itemId}
          eventTitle={detail.title}
          eventDescription={detail.description || ''}
          eventLocation={detail.location || ''}
          eventTime={dateString}
          eventMeetUrl={detail.meetUrl || ''}
          guests={detail.attendees || []}
          onClose={() => setIsEmailModalOpen(false)}
        />
      )}
    </>
  );
};
