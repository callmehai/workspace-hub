import React, { useLayoutEffect, useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  AlignLeft,
  AlertCircle,
  Bell,
  Calendar,
  ExternalLink,
  Link,
  Loader2,
  Mail,
  MapPin,
  MoreVertical,
  Paperclip,
  Pencil,
  Trash2,
  Users,
  Video,
  X,
} from 'lucide-react';
import toast from 'react-hot-toast';
import { itemsApi } from '../../lib/itemsApi';
import { useI18n } from '../../hooks/useI18n';
import { handleApiError } from '../../lib/errorUtils';
import type { CalendarEventAttendeeDto, EventReminderDto, CalendarEventDetailResponse, CalendarDriveAttachmentDto } from '../../types/items';
import { calendarEntryAccentDot } from '../../lib/calendarEntryVisuals';
import { SendEventEmailModal } from './SendEventEmailModal';

interface EventDetailPopupProps {
  itemId: string;
  /** Màu ô vuông đầu title — từ entry đã click trên lịch. */
  accentDotClass?: string;
  anchorRect?: DOMRect | null;
  onClose: () => void;
  onEdit: (detail: CalendarEventDetailResponse) => void;
  onDelete: () => void;
}

const POPUP_MARGIN = 12;
const POPUP_GAP = 10;

function clamp(value: number, min: number, max: number) {
  return Math.min(Math.max(value, min), max);
}

function responseLabel(response: string, lang: 'vi' | 'en') {
  if (response === 'accepted') return lang === 'vi' ? 'Có' : 'Yes';
  if (response === 'declined') return lang === 'vi' ? 'Không' : 'No';
  if (response === 'tentative') return lang === 'vi' ? 'Có thể' : 'Maybe';
  return lang === 'vi' ? 'Chưa trả lời' : 'No response';
}

function recurrenceSummary(recurrence: string[] | undefined, lang: 'vi' | 'en') {
  const rule = recurrence?.[0];
  if (!rule?.startsWith('RRULE:')) return null;
  if (rule.includes('FREQ=DAILY')) return lang === 'vi' ? 'Lap lai hang ngay' : 'Repeats daily';
  if (rule.includes('FREQ=WEEKLY')) return lang === 'vi' ? 'Lap lai hang tuan' : 'Repeats weekly';
  if (rule.includes('FREQ=MONTHLY')) return lang === 'vi' ? 'Lap lai hang thang' : 'Repeats monthly';
  if (rule.includes('FREQ=YEARLY')) return lang === 'vi' ? 'Lap lai hang nam' : 'Repeats yearly';
  return lang === 'vi' ? 'Lap lai dinh ky' : 'Repeats';
}

function reminderTypeLabel(reminderType: string, lang: 'vi' | 'en') {
  if (reminderType === 'InApp') return lang === 'vi' ? 'Trong ứng dụng' : 'In app';
  if (reminderType === 'GoogleEmail') return 'Google email';
  return 'Google popup';
}

function reminderText(reminder: EventReminderDto, lang: 'vi' | 'en', t: ReturnType<typeof useI18n>['t']) {
  const type = reminderTypeLabel(reminder.reminderType ?? 'GooglePopup', lang);
  const unit = String(reminder.offsetUnit ?? '').toLowerCase();
  const unitLabel = unit === 'minutes'
    ? t('calendar.minutes')
    : unit === 'hours'
      ? t('calendar.hours')
      : unit === 'days'
        ? t('calendar.days')
        : t('calendar.weeks');
  const at = reminder.timeOfDay ? (lang === 'vi' ? ` luc ${reminder.timeOfDay}` : ` at ${reminder.timeOfDay}`) : '';
  return `${type}: ${reminder.offsetValue} ${unitLabel}${at} ${t('calendar.before')}`;
}

function PopupRow({
  icon,
  align = 'start',
  children,
}: {
  icon: React.ReactNode;
  align?: 'start' | 'center';
  children: React.ReactNode;
}) {
  return (
    <div className={`grid grid-cols-[20px_minmax(0,1fr)] gap-3 text-[13px] leading-5 text-slate-700 dark:text-slate-200 ${align === 'center' ? 'items-center' : 'items-start'}`}>
      <div className="flex h-5 items-center justify-center text-slate-500 dark:text-slate-400">{icon}</div>
      <div className="min-w-0">{children}</div>
    </div>
  );
}

export const EventDetailPopup: React.FC<EventDetailPopupProps> = ({
  itemId,
  accentDotClass,
  anchorRect,
  onClose,
  onEdit,
  onDelete,
}) => {
  const { t, lang } = useI18n();
  const dl = lang === 'vi' ? 'vi-VN' : 'en-US';
  const queryClient = useQueryClient();
  const popoverRef = useRef<HTMLDivElement>(null);
  const [isEmailPopupOpen, setIsEmailPopupOpen] = useState(false);
  const [showMoreActions, setShowMoreActions] = useState(false);
  const [popoverStyle, setPopoverStyle] = useState<React.CSSProperties>({
    top: 96,
    left: 96,
    maxHeight: 'calc(100vh - 24px)',
  });

  const { data: detail, isLoading, isError, refetch } = useQuery({
    queryKey: ['calendar-event-detail', itemId],
    queryFn: () => itemsApi.getCalendarEventDetail(itemId),
    enabled: !!itemId,
  });

  const rsvpMutation = useMutation({
    mutationFn: (status: string) => itemsApi.rsvpEvent(itemId, status),
    onSuccess: () => {
      toast.success(t('calendar.rsvpSuccess'));
      queryClient.invalidateQueries({ queryKey: ['calendar-event-detail', itemId] });
      queryClient.invalidateQueries({ queryKey: ['calendar-items'] });
    },
    onError: (err) => handleApiError(err, t('calendar.rsvpUpdateFailed')),
  });

  useLayoutEffect(() => {
    const placePopover = () => {
      const viewportWidth = window.innerWidth;
      const viewportHeight = window.innerHeight;
      const rect = popoverRef.current?.getBoundingClientRect();
      const width = Math.min(rect?.width ?? 560, viewportWidth - POPUP_MARGIN * 2);
      const height = Math.min(rect?.height ?? 360, viewportHeight - POPUP_MARGIN * 2);

      let left = Math.max(POPUP_MARGIN, (viewportWidth - width) / 2);
      let top = Math.max(POPUP_MARGIN, (viewportHeight - height) / 2);

      if (anchorRect) {
        const rightSide = anchorRect.right + POPUP_GAP;
        const leftSide = anchorRect.left - width - POPUP_GAP;
        left = rightSide + width <= viewportWidth - POPUP_MARGIN
          ? rightSide
          : leftSide >= POPUP_MARGIN
            ? leftSide
            : clamp(anchorRect.left, POPUP_MARGIN, viewportWidth - width - POPUP_MARGIN);
        top = clamp(anchorRect.top + anchorRect.height / 2 - height / 2, POPUP_MARGIN, viewportHeight - height - POPUP_MARGIN);
      }

      setPopoverStyle({ top, left, maxHeight: viewportHeight - POPUP_MARGIN * 2 });
    };

    placePopover();
    window.addEventListener('resize', placePopover);
    return () => window.removeEventListener('resize', placePopover);
  }, [anchorRect, detail, isLoading, isError, showMoreActions]);

  const dateString = useMemo(() => {
    if (!detail?.start) return '';
    const start = new Date(detail.start);
    const end = detail.end ? new Date(detail.end) : null;
    const date = start.toLocaleDateString(dl, { weekday: 'long', month: 'long', day: 'numeric' });
    if (detail.allDay || !end) return detail.allDay ? `${date} (${t('calendar.allDay')})` : date;
    const startTime = start.toLocaleTimeString(dl, { hour: 'numeric', minute: '2-digit' });
    const endTime = end.toLocaleTimeString(dl, { hour: 'numeric', minute: '2-digit' });
    return `${date}, ${startTime} - ${endTime}`;
  }, [detail, dl, t]);

  const currentUserEmail = detail?.owningCalendarName;
  const organizerEmail = detail?.organizerEmail;
  const isOwner = !organizerEmail || organizerEmail.toLowerCase() === currentUserEmail?.toLowerCase();
  const userRsvp = detail?.attendees?.find(
    (a: CalendarEventAttendeeDto) => a.email.toLowerCase() === currentUserEmail?.toLowerCase(),
  );
  const currentResponse = userRsvp?.responseStatus || 'needsAction';
  const guestCount = detail?.attendees?.length ?? 0;
  const acceptedCount = detail?.attendees?.filter((a: CalendarEventAttendeeDto) => a.responseStatus === 'accepted').length ?? 0;
  const recurrence = recurrenceSummary(detail?.recurrence, lang);
  const titleAccentDot = accentDotClass ?? calendarEntryAccentDot('event', detail?.allDay === true);

  const handleCopyLink = () => {
    const link = detail?.htmlLink || `${window.location.origin}/calendar?eventId=${itemId}`;
    navigator.clipboard.writeText(link);
    toast.success(t('calendar.copiedShareLink'));
  };

  const shell = (children: React.ReactNode) => (
    <>
      <div className="fixed inset-0 z-[8000]" onMouseDown={onClose}>
        <div
          ref={popoverRef}
          role="dialog"
          aria-modal="false"
          style={popoverStyle}
          className="fixed w-[min(520px,calc(100vw-24px))] overflow-hidden rounded-[24px] border border-slate-200 bg-slate-100 shadow-2xl ring-1 ring-black/5 dark:border-slate-800 dark:bg-slate-950"
          onMouseDown={event => event.stopPropagation()}
        >
          {children}
        </div>
      </div>
      {detail && isEmailPopupOpen && (
        <SendEventEmailModal
          itemId={itemId}
          eventTitle={detail.title}
          eventDescription={detail.description || ''}
          eventLocation={detail.location || ''}
          eventTime={dateString}
          eventMeetUrl={detail.meetUrl || ''}
          guests={detail.attendees || []}
          onClose={() => setIsEmailPopupOpen(false)}
        />
      )}
    </>
  );

  if (isLoading) {
    return shell(
      <div className="flex min-h-36 flex-col items-center justify-center gap-3 p-6">
        <Loader2 className="h-7 w-7 animate-spin text-brand-500" />
        <span className="text-sm font-medium text-slate-500 dark:text-slate-400">{t('calendar.loadingDetail')}</span>
      </div>,
    );
  }

  if (isError || !detail) {
    return shell(
      <div className="flex min-h-44 flex-col items-center justify-center p-6 text-center">
        <AlertCircle className="mb-3 h-10 w-10 text-rose-500" />
        <h3 className="text-sm font-semibold text-slate-800 dark:text-slate-100">{t('item.loadError')}</h3>
        <p className="mt-1 text-xs text-slate-500 dark:text-slate-400">{t('item.loadErrorHint')}</p>
        <div className="mt-4 flex gap-2">
          <button type="button" onClick={() => refetch()} className="rounded-lg bg-brand-600 px-3.5 py-2 text-xs font-semibold text-white hover:bg-brand-700">
            {t('item.reload')}
          </button>
          <button type="button" onClick={onClose} className="rounded-lg bg-white px-3.5 py-2 text-xs font-semibold text-slate-600 hover:bg-slate-50 dark:bg-slate-800 dark:text-slate-200">
            {t('common.close')}
          </button>
        </div>
      </div>,
    );
  }

  return shell(
    <>
      <div className="flex items-center justify-end gap-2.5 px-4 py-3.5">
        {detail.canEdit && (
          <button type="button" onClick={() => onEdit(detail)} title={t('common.edit')} className="rounded-full p-1.5 text-slate-600 hover:bg-slate-200/70 hover:text-slate-900 dark:text-slate-300 dark:hover:bg-slate-800">
            <Pencil className="h-4 w-4" />
          </button>
        )}
        {detail.isOrganizer && (
          <button type="button" onClick={onDelete} title={t('common.delete')} className="rounded-full p-1.5 text-slate-600 hover:bg-rose-50 hover:text-rose-600 dark:text-slate-300 dark:hover:bg-rose-950/30 dark:hover:text-rose-300">
            <Trash2 className="h-4 w-4" />
          </button>
        )}
        <button type="button" onClick={() => setIsEmailPopupOpen(true)} title={t('calendar.emailGuests')} className="rounded-full p-1.5 text-slate-600 hover:bg-slate-200/70 hover:text-slate-900 dark:text-slate-300 dark:hover:bg-slate-800">
          <Mail className="h-4 w-4" />
        </button>
        <div className="relative">
          <button type="button" onClick={() => setShowMoreActions(current => !current)} className="rounded-full p-1.5 text-slate-600 hover:bg-slate-200/70 hover:text-slate-900 dark:text-slate-300 dark:hover:bg-slate-800">
            <MoreVertical className="h-4 w-4" />
          </button>
          {showMoreActions && (
            <div className="absolute right-0 top-10 z-10 w-44 overflow-hidden rounded-xl border border-slate-200 bg-white py-1 shadow-xl ring-1 ring-black/5 dark:border-slate-700 dark:bg-slate-900 dark:ring-white/10">
              <button type="button" onClick={() => { handleCopyLink(); setShowMoreActions(false); }} className="flex w-full items-center gap-2 px-3.5 py-2 text-left text-xs font-semibold text-slate-700 hover:bg-slate-50 hover:text-slate-950 dark:text-slate-100 dark:hover:bg-slate-800 dark:hover:text-white">
                <Link className="h-3.5 w-3.5 text-slate-400 dark:text-slate-300" />
                {t('calendar.copyLink')}
              </button>
              {detail.htmlLink && (
                <a href={detail.htmlLink} target="_blank" rel="noopener noreferrer" onClick={() => setShowMoreActions(false)} className="flex items-center gap-2 px-3.5 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-50 hover:text-slate-950 dark:text-slate-100 dark:hover:bg-slate-800 dark:hover:text-white">
                  <ExternalLink className="h-3.5 w-3.5 text-slate-400 dark:text-slate-300" />
                  {t('calendar.viewGoogle')}
                </a>
              )}
            </div>
          )}
        </div>
        <button type="button" onClick={onClose} className="rounded-full p-1.5 text-slate-600 hover:bg-slate-200/70 hover:text-slate-900 dark:text-slate-300 dark:hover:bg-slate-800">
          <X className="h-4.5 w-4.5" />
        </button>
      </div>

      <div className="max-h-[calc(100vh-108px)] overflow-y-auto px-7 pb-7 pt-1">
        <div className="grid grid-cols-[16px_minmax(0,1fr)] gap-5">
          <span className={`mt-2 h-3.5 w-3.5 shrink-0 rounded ${titleAccentDot}`} />
          <div className="min-w-0">
            <h2 className="break-words text-[24px] font-normal leading-tight text-slate-900 dark:text-slate-50">{detail.title}</h2>
            {dateString && <p className="mt-1 text-[13px] text-slate-700 dark:text-slate-300">{dateString}</p>}
          </div>
        </div>

        <div className="mt-6 space-y-3.5">
          {detail.description && (
            <PopupRow icon={<AlignLeft className="h-4 w-4" />}>
              <p className="whitespace-pre-wrap break-words">{detail.description}</p>
            </PopupRow>
          )}

          {detail.driveAttachments?.length > 0 && (
            <PopupRow icon={<Paperclip className="h-4 w-4" />} align="center">
              <div className="flex flex-wrap gap-1.5">
                {detail.driveAttachments.map((attachment: CalendarDriveAttachmentDto, idx: number) => (
                  <a
                    key={`${attachment.fileId ?? attachment.fileUrl ?? idx}`}
                    href={attachment.fileUrl ?? undefined}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex h-8 max-w-[170px] items-center gap-1.5 rounded-md border border-slate-300 bg-slate-50 px-2 text-xs font-semibold text-slate-700 shadow-sm hover:bg-white dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200"
                  >
                    <Paperclip className="h-3 w-3 shrink-0 text-rose-500" />
                    <span className="truncate">{attachment.title || attachment.fileId || 'Attachment'}</span>
                  </a>
                ))}
              </div>
            </PopupRow>
          )}

          {detail.meetUrl && (
            <PopupRow icon={<Video className="h-4 w-4" />}>
              <a href={detail.meetUrl} target="_blank" rel="noopener noreferrer" className="font-semibold text-brand-600 hover:underline dark:text-brand-300">
                {t('calendar.joinMeet')}
              </a>
            </PopupRow>
          )}

          {detail.location && (
            <PopupRow icon={<MapPin className="h-4 w-4" />}>
              <span className="break-words">{detail.location}</span>
            </PopupRow>
          )}

          {detail.reminders?.length > 0 && (
            <PopupRow icon={<Bell className="h-4 w-4" />}>
              <div className="space-y-1">
                {detail.reminders.map((reminder: EventReminderDto, idx: number) => (
                  <p key={idx}>{reminderText(reminder, lang, t)}</p>
                ))}
              </div>
            </PopupRow>
          )}

          {recurrence && (
            <PopupRow icon={<Calendar className="h-4 w-4" />}>
              <span>{recurrence}</span>
            </PopupRow>
          )}

          {detail.owningCalendarName && (
            <PopupRow icon={<Calendar className="h-4 w-4" />}>
              <span>{detail.owningCalendarName}</span>
            </PopupRow>
          )}

          {guestCount > 0 && (
            <PopupRow icon={<Users className="h-4 w-4" />}>
              <span>
                {guestCount} {t('calendar.guestCount')}
                {acceptedCount > 0 ? ` - ${acceptedCount} ${t('calendar.acceptedCount')}` : ''}
              </span>
            </PopupRow>
          )}

          {!isOwner && (
            <PopupRow icon={<Users className="h-4 w-4" />}>
              <div className="flex flex-wrap items-center gap-2">
                <span className="mr-1 text-slate-500 dark:text-slate-400">{responseLabel(currentResponse, lang)}</span>
                <button type="button" disabled={rsvpMutation.isPending} onClick={() => rsvpMutation.mutate('accepted')} className={`rounded-full px-3 py-1 text-xs font-semibold ${currentResponse === 'accepted' ? 'bg-emerald-600 text-white' : 'bg-white text-slate-700 hover:bg-slate-50 dark:bg-slate-800 dark:text-slate-200'}`}>
                  {t('calendar.rsvpYes')}
                </button>
                <button type="button" disabled={rsvpMutation.isPending} onClick={() => rsvpMutation.mutate('tentative')} className={`rounded-full px-3 py-1 text-xs font-semibold ${currentResponse === 'tentative' ? 'bg-amber-600 text-white' : 'bg-white text-slate-700 hover:bg-slate-50 dark:bg-slate-800 dark:text-slate-200'}`}>
                  {t('calendar.rsvpMaybe')}
                </button>
                <button type="button" disabled={rsvpMutation.isPending} onClick={() => rsvpMutation.mutate('declined')} className={`rounded-full px-3 py-1 text-xs font-semibold ${currentResponse === 'declined' ? 'bg-rose-600 text-white' : 'bg-white text-slate-700 hover:bg-slate-50 dark:bg-slate-800 dark:text-slate-200'}`}>
                  {t('calendar.rsvpNo')}
                </button>
              </div>
            </PopupRow>
          )}
        </div>
      </div>
    </>,
  );
};
