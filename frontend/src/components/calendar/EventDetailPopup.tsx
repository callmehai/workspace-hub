import React, { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
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
  Plus,
  Trash2,
  Users,
  X,
} from 'lucide-react';
import toast from 'react-hot-toast';
import { itemsApi, foldersApi } from '../../lib/itemsApi';
import { useI18n } from '../../hooks/useI18n';
import { handleApiError } from '../../lib/errorUtils';
import type { CalendarEventAttendeeDto, EventReminderDto, CalendarEventDetailResponse, CalendarDriveAttachmentDto, FolderResponse } from '../../types/items';

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
/** Lề đáy rộng hơn một chút để popup không dính mép màn. */
const POPUP_BOTTOM_MARGIN = 28;
const POPUP_GAP = 10;
/** Khi bị kẹt đáy, kéo bao nhiêu % khoảng cách tới giữa màn hình. */
const BOTTOM_LIFT_TOWARD_CENTER = 0.7;

function clamp(value: number, min: number, max: number) {
  return Math.min(Math.max(value, min), max);
}

/** top / left theo viewport, ưu tiên sát anchor; nếu sát đáy thì kéo lên hướng giữa. */
function computePopoverPosition(anchorRect: DOMRect, width: number, height: number) {
  const viewportWidth = window.innerWidth;
  const viewportHeight = window.innerHeight;
  const minLeft = POPUP_MARGIN;
  const maxLeft = Math.max(minLeft, viewportWidth - width - POPUP_MARGIN);
  const minTop = POPUP_MARGIN;
  const maxTop = Math.max(minTop, viewportHeight - height - POPUP_BOTTOM_MARGIN);

  const rightSide = anchorRect.right + POPUP_GAP;
  const leftSide = anchorRect.left - width - POPUP_GAP;
  const left = rightSide + width <= viewportWidth - POPUP_MARGIN
    ? rightSide
    : leftSide >= POPUP_MARGIN
      ? leftSide
      : clamp(anchorRect.left, minLeft, maxLeft);

  // Ưu tiên căn mép trên với event (sát chip).
  const preferredTop = anchorRect.top;
  let top = clamp(preferredTop, minTop, maxTop);

  // Event/nội dung làm preferred vượt đáy → kéo lên hướng giữa, vẫn giữ giao vùng event.
  if (preferredTop > maxTop) {
    const centerTop = (viewportHeight - height) / 2;
    const lifted = Math.round(maxTop - Math.max(0, maxTop - centerTop) * BOTTOM_LIFT_TOWARD_CENTER);
    top = clamp(lifted, minTop, maxTop);

    const anchorMid = anchorRect.top + anchorRect.height / 2;
    // Đảm bảo tâm event vẫn nằm trong (hoặc sát) khung popup.
    if (anchorMid < top + 8) {
      top = clamp(anchorMid - 8, minTop, maxTop);
    } else if (anchorMid > top + height - 8) {
      top = clamp(anchorMid - height + 8, minTop, maxTop);
    }
  }

  return {
    top,
    left,
    maxHeight: viewportHeight - POPUP_MARGIN - POPUP_BOTTOM_MARGIN,
  };
}

function responseLabel(response: string, t: ReturnType<typeof useI18n>['t']) {
  if (response === 'accepted') return t('calendar.rsvpYes');
  if (response === 'declined') return t('calendar.rsvpNo');
  if (response === 'tentative') return t('calendar.rsvpMaybe');
  return t('calendar.noResponse');
}

function recurrenceSummary(recurrence: string[] | undefined, t: ReturnType<typeof useI18n>['t']) {
  const rule = recurrence?.[0];
  if (!rule?.startsWith('RRULE:')) return null;
  if (rule.includes('FREQ=DAILY')) return t('calendar.repeatsDaily');
  if (rule.includes('FREQ=WEEKLY')) return t('calendar.repeatsWeekly');
  if (rule.includes('FREQ=MONTHLY')) return t('calendar.repeatsMonthly');
  if (rule.includes('FREQ=YEARLY')) return t('calendar.repeatsYearly');
  return t('calendar.repeatsGeneric');
}

function reminderTypeLabel(reminderType: string, t: ReturnType<typeof useI18n>['t']) {
  if (reminderType === 'InApp') return t('calendar.inAppShort');
  if (reminderType === 'GoogleEmail') return t('calendar.googleEmailReminder');
  return t('calendar.googlePopupReminder');
}

function reminderText(reminder: EventReminderDto, t: ReturnType<typeof useI18n>['t']) {
  const type = reminderTypeLabel(reminder.reminderType ?? 'GooglePopup', t);
  const unit = String(reminder.offsetUnit ?? '').toLowerCase();
  const unitLabel = unit === 'minutes'
    ? t('calendar.minutes')
    : unit === 'hours'
      ? t('calendar.hours')
      : unit === 'days'
        ? t('calendar.days')
        : t('calendar.weeks');
  const at = reminder.timeOfDay ? t('calendar.atTime', { time: reminder.timeOfDay }) : '';
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

  const [isAddingToFolder, setIsAddingToFolder] = useState(false);
  const [folderSearch, setFolderSearch] = useState('');
  const addFolderRef = useRef<HTMLDivElement>(null);

  const closeFolderPicker = () => {
    setIsAddingToFolder(false);
    setFolderSearch('');
  };

  useEffect(() => {
    if (!isAddingToFolder) return;
    const onDocClick = (e: MouseEvent) => {
      if (addFolderRef.current && !addFolderRef.current.contains(e.target as Node)) {
        closeFolderPicker();
      }
    };
    document.addEventListener('mousedown', onDocClick, true);
    return () => document.removeEventListener('mousedown', onDocClick, true);
  }, [isAddingToFolder]);

  const { data: item } = useQuery({
    queryKey: ['item', itemId],
    queryFn: () => itemsApi.getItemById(itemId),
    enabled: !!itemId,
  });

  const { data: folders = [] } = useQuery({
    queryKey: ['folders'],
    queryFn: () => foldersApi.getFolders(false),
  });

  const addToFolderMutation = useMutation({
    mutationFn: (folderId: string) => foldersApi.addItemToFolder(folderId, { itemId }),
    onSuccess: () => {
      toast.success(t('calendar.addedToFolder'));
      queryClient.invalidateQueries({ queryKey: ['item', itemId] });
      queryClient.invalidateQueries({ queryKey: ['calendar-items'] });
    },
    onError: (err) => handleApiError(err, t('calendar.addToFolderFailed')),
  });

  const removeFromFolderMutation = useMutation({
    mutationFn: (folderId: string) => foldersApi.removeItemFromFolder(folderId, itemId),
    onSuccess: () => {
      toast.success(t('calendar.removedFromFolder'));
      queryClient.invalidateQueries({ queryKey: ['item', itemId] });
      queryClient.invalidateQueries({ queryKey: ['calendar-items'] });
    },
    onError: (err) => handleApiError(err, t('calendar.removeFromFolderFailed')),
  });

  useLayoutEffect(() => {
    const placePopover = () => {
      const viewportWidth = window.innerWidth;
      const viewportHeight = window.innerHeight;
      const rect = popoverRef.current?.getBoundingClientRect();
      const width = Math.min(rect?.width ?? 560, viewportWidth - POPUP_MARGIN * 2);
      const height = Math.min(
        rect?.height ?? 360,
        viewportHeight - POPUP_MARGIN - POPUP_BOTTOM_MARGIN,
      );

      if (!anchorRect) {
        setPopoverStyle({
          top: Math.max(POPUP_MARGIN, (viewportHeight - height) / 2),
          left: Math.max(POPUP_MARGIN, (viewportWidth - width) / 2),
          maxHeight: viewportHeight - POPUP_MARGIN - POPUP_BOTTOM_MARGIN,
        });
        return;
      }

      setPopoverStyle(computePopoverPosition(anchorRect, width, height));
    };

    placePopover();
    // Remeasure after paint — height ổn định khi content (detail) đã render.
    const raf = window.requestAnimationFrame(placePopover);
    window.addEventListener('resize', placePopover);
    return () => {
      window.cancelAnimationFrame(raf);
      window.removeEventListener('resize', placePopover);
    };
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
  const guests = detail?.attendees?.filter((a: CalendarEventAttendeeDto) => !a.organizer) ?? [];
  const guestCount = guests.length;
  const acceptedCount = guests.filter((a: CalendarEventAttendeeDto) => a.responseStatus === 'accepted').length;
  const recurrence = recurrenceSummary(detail?.recurrence, t);
  const titleAccentDot = accentDotClass ?? (isOwner ? 'bg-emerald-500' : 'bg-amber-500');

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
          guests={guests}
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

        {/* Folders Assignment Section */}
        <div className="mt-4 flex flex-wrap items-center gap-1.5 border-t border-slate-200/50 pt-3 dark:border-slate-800/50 pl-[36px]">
          <span className="text-[12px] font-semibold text-slate-400 dark:text-slate-500 mr-1.5">{t('calendar.foldersColon')}</span>
          {item?.folderIds?.map(fId => {
            const f = folders.find((fol: FolderResponse) => fol.id === fId);
            if (!f) return null;
            return (
              <span
                key={f.id}
                style={{ borderColor: f.color || '#94a3b8', color: f.color || '#64748b' }}
                className="inline-flex items-center gap-1.5 rounded-full border bg-white dark:bg-slate-900 py-0.5 pl-2 pr-1 text-[11px] font-semibold shadow-sm transition hover:bg-slate-50 dark:hover:bg-slate-800"
              >
                <span className="w-1.5 h-1.5 rounded-full shrink-0" style={{ backgroundColor: f.color || '#94a3b8' }} />
                <span className="truncate max-w-[120px]">{f.name}</span>
                <button
                  type="button"
                  onClick={() => removeFromFolderMutation.mutate(f.id)}
                  className="p-0.5 rounded-full hover:bg-slate-100 dark:hover:bg-slate-800 hover:text-rose-600 transition"
                  title={t('calendar.removeFromFolder')}
                >
                  <X className="w-3 h-3" />
                </button>
              </span>
            );
          })}

          {/* Add folder button & dropdown */}
          <div className="relative" ref={addFolderRef}>
            <button
              type="button"
              onClick={() => {
                if (isAddingToFolder) closeFolderPicker();
                else {
                  setFolderSearch('');
                  setIsAddingToFolder(true);
                }
              }}
              className="inline-flex items-center justify-center gap-1 h-[24px] px-2.5 rounded-full bg-slate-200/50 dark:bg-slate-800 border border-slate-300 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-200 dark:hover:bg-slate-700 hover:text-slate-800 dark:hover:text-white transition-colors text-[11px] font-semibold"
              title={t('calendar.addToFolder')}
            >
              <Plus className="w-3 h-3" />
              <span>{t('calendar.add')}</span>
            </button>

            {isAddingToFolder && (
              <div className="absolute top-full left-0 mt-1.5 w-48 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 shadow-xl rounded-xl py-1.5 z-[9000] animate-in fade-in zoom-in-95 duration-100">
                <div className="px-2 py-1.5 border-b border-slate-100 dark:border-slate-700">
                  <input
                    type="text"
                    placeholder={t('calendar.searchFolders')}
                    value={folderSearch}
                    onChange={e => setFolderSearch(e.target.value)}
                    className="w-full px-2 py-1 text-xs bg-slate-50 dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded outline-none focus:border-brand-500 focus:ring-1 focus:ring-brand-500/20 text-slate-900 dark:text-slate-100"
                  />
                </div>
                {folders.filter((f: FolderResponse) => !item?.folderIds?.includes(f.id)).length === 0 ? (
                  <div className="px-3 py-2 text-xs text-slate-500 dark:text-slate-400 text-center">{t('calendar.alreadyInAllFolders')}</div>
                ) : folders.filter((f: FolderResponse) => !item?.folderIds?.includes(f.id) && f.name.toLowerCase().includes(folderSearch.toLowerCase())).length === 0 ? (
                  <div className="px-3 py-2 text-xs text-slate-500 dark:text-slate-400 text-center">{t('calendar.noFoldersFound')}</div>
                ) : (
                  folders
                    .filter((f: FolderResponse) => !item?.folderIds?.includes(f.id) && f.name.toLowerCase().includes(folderSearch.toLowerCase()))
                    .map((f: FolderResponse) => (
                      <button
                        key={f.id}
                        type="button"
                        onClick={() => {
                          addToFolderMutation.mutate(f.id);
                          closeFolderPicker();
                        }}
                        disabled={addToFolderMutation.isPending}
                        className="w-full text-left px-3.5 py-2 text-xs font-semibold text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-800 flex items-center gap-2.5 transition-colors"
                      >
                        <span className="w-2 h-2 rounded-full" style={{ backgroundColor: f.color || '#f59e0b' }}></span>
                        <span className="truncate">{f.name}</span>
                      </button>
                    ))
                )}
              </div>
            )}
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

          {detail.location && (
            <PopupRow icon={<MapPin className="h-4 w-4" />}>
              <span className="break-words">{detail.location}</span>
            </PopupRow>
          )}

          {detail.reminders?.length > 0 && (
            <PopupRow icon={<Bell className="h-4 w-4" />}>
              <div className="space-y-1">
                {detail.reminders.map((reminder: EventReminderDto, idx: number) => (
                  <p key={idx}>{reminderText(reminder, t)}</p>
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
                <span className="mr-1 text-slate-500 dark:text-slate-400">{responseLabel(currentResponse, t)}</span>
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
