import { CalendarDays, Clock3, Loader2, MapPin, Users, X } from 'lucide-react';
import type { CalendarInvitation, CalendarInvitationStatus } from '../../lib/calendarInvitationsApi';
import { useI18n } from '../../hooks/useI18n';

interface Props {
  invitation: CalendarInvitation;
  saving?: boolean;
  onRespond: (status: Exclude<CalendarInvitationStatus, 'NeedsAction'>) => void;
  onClose: () => void;
}

export function CalendarInvitationDialog({ invitation, saving = false, onRespond, onClose }: Props) {
  const { t, lang } = useI18n();
  const locale = lang === 'vi' ? 'vi-VN' : 'en-US';
  const start = new Date(invitation.start);
  const end = new Date(invitation.end);
  const dateText = invitation.allDay
    ? start.toLocaleDateString(locale, { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' })
    : `${start.toLocaleString(locale, { weekday: 'long', day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })} – ${end.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' })}`;
  const needsAction = invitation.status === 'NeedsAction';
  const responseLabel =
    invitation.status === 'Accepted' ? t('calendar.rsvpYes')
      : invitation.status === 'Declined' ? t('calendar.rsvpNo')
        : invitation.status === 'Tentative' ? t('calendar.rsvpMaybe')
          : null;
  const responseChipClass =
    invitation.status === 'Accepted' ? 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300'
      : invitation.status === 'Declined' ? 'bg-rose-50 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300'
        : invitation.status === 'Tentative' ? 'bg-amber-50 text-amber-800 dark:bg-amber-500/15 dark:text-amber-300'
          : 'bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300';

  return (
    <div className="fixed inset-0 z-[9000] flex items-center justify-center bg-slate-950/45 p-4 backdrop-blur-[2px]" onMouseDown={onClose}>
      <div className="w-full max-w-lg rounded-2xl border border-slate-200 bg-white p-6 shadow-2xl dark:border-slate-700 dark:bg-slate-900" onMouseDown={event => event.stopPropagation()}>
        <div className="flex items-start justify-between gap-4">
          <div className="flex min-w-0 gap-3">
            <span className="mt-0.5 grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-brand-50 text-brand-600 dark:bg-brand-500/15 dark:text-brand-300">
              <CalendarDays className="h-5 w-5" />
            </span>
            <div className="min-w-0">
              <p className="text-xs font-semibold uppercase tracking-wide text-brand-600 dark:text-brand-300">
                {t('calendar.invitationTitle')}
              </p>
              <h2 className="mt-1 break-words text-xl font-bold text-slate-900 dark:text-white">{invitation.title}</h2>
              <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">
                {t('calendar.organizer')}: {invitation.organizerName} · {invitation.organizerEmail}
              </p>
            </div>
          </div>
          <button type="button" onClick={onClose} disabled={saving} className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800">
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="mt-6 space-y-3 text-sm text-slate-600 dark:text-slate-300">
          <div className="flex gap-3"><Clock3 className="mt-0.5 h-4 w-4 shrink-0 text-slate-400" /><span>{dateText}</span></div>
          {invitation.location && <div className="flex gap-3"><MapPin className="mt-0.5 h-4 w-4 shrink-0 text-slate-400" /><span>{invitation.location}</span></div>}
          {invitation.attendees.length > 0 && <div className="flex gap-3"><Users className="mt-0.5 h-4 w-4 shrink-0 text-slate-400" /><span className="break-all">{invitation.attendees.join(', ')}</span></div>}
          {invitation.description && <p className="whitespace-pre-wrap rounded-xl bg-slate-50 p-3 text-sm dark:bg-slate-800/70">{invitation.description}</p>}
          {invitation.googleSyncPending && invitation.status !== 'NeedsAction' && (
            <p className="rounded-lg bg-amber-50 px-3 py-2 text-xs font-medium text-amber-700 dark:bg-amber-500/10 dark:text-amber-300">
              {t('calendar.invitationPendingSync')}
            </p>
          )}
        </div>

        <div className="mt-6 flex flex-wrap items-center justify-end gap-2 border-t border-slate-100 pt-4 dark:border-slate-800">
          {saving && <Loader2 className="mr-auto h-5 w-5 animate-spin text-brand-500" />}
          {needsAction ? (
            <>
              <button type="button" disabled={saving} onClick={() => onRespond('Declined')} className="h-9 rounded-lg px-3.5 text-sm font-semibold text-rose-600 hover:bg-rose-50 disabled:opacity-50 dark:hover:bg-rose-500/10">
                {t('calendar.decline')}
              </button>
              <button type="button" disabled={saving} onClick={() => onRespond('Tentative')} className="h-9 rounded-lg border border-slate-200 px-3.5 text-sm font-semibold text-slate-700 hover:bg-slate-50 disabled:opacity-50 dark:border-slate-700 dark:text-slate-200 dark:hover:bg-slate-800">
                {t('calendar.rsvpMaybe')}
              </button>
              <button type="button" disabled={saving} onClick={() => onRespond('Accepted')} className="h-9 rounded-lg bg-brand-600 px-4 text-sm font-semibold text-white hover:bg-brand-700 disabled:opacity-50">
                {t('calendar.accept')}
              </button>
            </>
          ) : (
            <>
              {responseLabel && (
                <span className={`mr-auto inline-flex items-center rounded-full px-3 py-1 text-xs font-semibold ${responseChipClass}`}>
                  {t('calendar.yourResponse')}: {responseLabel}
                </span>
              )}
              <button type="button" onClick={onClose} className="h-9 rounded-lg bg-brand-600 px-4 text-sm font-semibold text-white hover:bg-brand-700">
                {t('common.close')}
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
