import React, { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import {
  X, Mail, Send, Trash2, Loader2, AlertCircle, MessageSquare,
} from 'lucide-react';
import toast from 'react-hot-toast';
import {
  contactsApi,
  contactDisplayName,
  patchContactListCache,
  type ContactDetailDto,
  type ContactProfile,
} from '../../lib/contactsApi';
import { itemsApi } from '../../lib/itemsApi';
import { handleContactMutateError } from '../../lib/contactsError';
import { ConfirmDialog } from '../ConfirmDialog';
import { ContactProfileForm } from './ContactProfileForm';
import { normalizeProfile, profileFromContact } from './contactProfileUtils';
import { useI18n } from '../../hooks/useI18n';
import { timeAgo } from '../../lib/datetime';

interface ContactDetailPanelProps {
  contactId: string;
  connectionId: string;
  readOnlyMode: boolean;
  onClose: () => void;
  onUpdated?: () => void;
  onForbidden?: () => void;
}

function displayNameFrom(contact: ContactDetailDto): string {
  return contactDisplayName(contact);
}

function avatarLetter(name: string): string {
  const ch = name.trim().charAt(0);
  return ch ? ch.toUpperCase() : '?';
}

interface ContactDetailBodyProps {
  contact: ContactDetailDto;
  contactId: string;
  connectionId: string;
  readOnlyMode: boolean;
  onClose: () => void;
  onUpdated?: () => void;
  onForbidden?: () => void;
}

function ContactDetailBody({
  contact,
  contactId,
  connectionId,
  readOnlyMode,
  onClose,
  onUpdated,
  onForbidden,
}: ContactDetailBodyProps) {
  const { t, lang } = useI18n();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [profile, setProfile] = useState<ContactProfile>(() => profileFromContact(contact));
  const [etag, setEtag] = useState(() => contact.etag ?? '');
  const [deleteOpen, setDeleteOpen] = useState(false);

  const DRAWER_MIN = 420;
  const [drawerWidth, setDrawerWidth] = useState<number>(() => {
    const saved = Number(localStorage.getItem('wh-detail-width'));
    return saved >= DRAWER_MIN ? saved : 560;
  });
  const widthRef = useRef(drawerWidth);

  const startResize = (e: React.MouseEvent) => {
    e.preventDefault();
    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';
    const onMove = (ev: MouseEvent) => {
      const max = Math.min(1200, window.innerWidth * 0.95);
      const w = Math.max(DRAWER_MIN, Math.min(window.innerWidth - ev.clientX, max));
      widthRef.current = w;
      setDrawerWidth(w);
    };
    const onUp = () => {
      document.body.style.cursor = '';
      document.body.style.userSelect = '';
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mouseup', onUp);
      localStorage.setItem('wh-detail-width', String(Math.round(widthRef.current)));
    };
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
  };

  const participantEmail = contact.email ?? '';
  const { data: recentEmails } = useQuery({
    queryKey: ['items', 'participant', participantEmail],
    queryFn: () => itemsApi.getItems({
      types: ['Email'],
      participantEmail,
      limit: 10,
    }),
    enabled: !!participantEmail,
  });

  const saveMutation = useMutation({
    mutationFn: () => {
      if (!profile || !etag) throw new Error('missing');
      return contactsApi.updateContact(contactId, {
        etag,
        profile: normalizeProfile(profile, contact.email ?? undefined),
      });
    },
    onSuccess: (updated) => {
      toast.success(t('contacts.updated'));
      setEtag(updated.etag ?? etag);
      setProfile({
        ...updated.profile,
        emails: updated.profile.emails.map(e => ({ ...e })),
        phones: updated.profile.phones.map(p => ({ ...p })),
      });
      queryClient.invalidateQueries({ queryKey: ['contacts'] });
      queryClient.invalidateQueries({ queryKey: ['contact', contactId] });
      onUpdated?.();
    },
    onError: (err) => handleContactMutateError(err, t, t('contacts.updateFail'), {
      onForbidden: () => onForbidden?.(),
    }),
  });

  const deleteMutation = useMutation({
    mutationFn: () => contactsApi.deleteContact(contactId),
    onSuccess: () => {
      toast.success(t('contacts.deleted'));
      queryClient.invalidateQueries({ queryKey: ['contacts'] });
      onClose();
      onUpdated?.();
    },
    onError: (err) => handleContactMutateError(err, t, t('contacts.deleteFail'), {
      onForbidden: () => onForbidden?.(),
    }),
  });

  const editable = contact.source === 'Contact' && !contact.readOnly && !readOnlyMode;
  const name = displayNameFrom(contact);

  const openSendEmail = () => {
    if (!contact.email) return;
    const to = encodeURIComponent(contact.email);
    const conn = encodeURIComponent(connectionId);
    navigate(`/send-email?connectionId=${conn}&to=${to}`);
  };

  return (
    <div
      className="relative w-full max-w-[95vw] bg-white dark:bg-slate-900 border-l border-slate-200 dark:border-slate-800 shadow-2xl flex flex-col rounded-l-2xl overflow-hidden"
      style={{ width: drawerWidth, animation: 'wh-slide-in .25s ease' }}
    >
      <div
        onMouseDown={startResize}
        title={t('item.resizeHint')}
        className="group absolute inset-y-0 left-0 z-20 w-2 cursor-col-resize flex items-center justify-center"
      >
        <div className="h-full w-[3px] bg-transparent group-hover:bg-brand-400/70 transition-colors" />
      </div>

      <div className="px-5 py-4 border-b border-slate-200 dark:border-slate-800 shrink-0">
        <div className="flex items-start justify-between gap-3">
          <div className="flex items-center gap-3 min-w-0">
            <div className="w-11 h-11 rounded-xl bg-brand-50 text-brand-700 dark:bg-brand-500/15 dark:text-brand-300 flex items-center justify-center text-lg font-semibold shrink-0">
              {avatarLetter(name)}
            </div>
            <div className="min-w-0">
              <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-100 truncate">{name}</h2>
              <p className="text-sm text-slate-500 truncate">{contact.email ?? '—'}</p>
              <span className="inline-flex mt-1 px-2 py-0.5 rounded-full text-xs font-medium bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-300">
                {t('contacts.badgeSaved')}
              </span>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="p-1.5 text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 rounded-lg shrink-0"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        <div className="flex flex-wrap gap-2 mt-4">
          {editable && (
            <button
              type="button"
              disabled={saveMutation.isPending}
              onClick={() => saveMutation.mutate()}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-brand-600 text-white text-sm font-medium hover:bg-brand-700 disabled:opacity-50"
            >
              {saveMutation.isPending && <Loader2 className="w-4 h-4 animate-spin" />}
              {t('common.save')}
            </button>
          )}
          <button
            type="button"
            onClick={openSendEmail}
            disabled={!contact.email}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 text-sm font-medium hover:bg-slate-50 dark:hover:bg-slate-800 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <Send className="w-4 h-4" />
            {t('contacts.sendEmail')}
          </button>
          {editable && (
            <button
              type="button"
              onClick={() => setDeleteOpen(true)}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg border border-rose-200 text-rose-600 text-sm font-medium hover:bg-rose-50 dark:border-rose-500/30 dark:hover:bg-rose-500/10"
            >
              <Trash2 className="w-4 h-4" />
              {t('common.delete')}
            </button>
          )}
        </div>
      </div>

      <div className="flex-1 overflow-y-auto px-5 py-5">
        <ContactProfileForm
          profile={profile}
          onChange={setProfile}
          disabled={!editable}
        />

        <section className="mt-8 pt-6 border-t border-slate-100 dark:border-slate-800">
          <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-slate-400 mb-3">
            <MessageSquare className="w-4 h-4" />
            {t('contacts.recentInteractions')}
          </div>
          {!recentEmails?.items.length ? (
            <p className="text-sm text-slate-400">{t('contacts.recentEmpty')}</p>
          ) : (
            <ul className="space-y-2">
              {recentEmails.items.map(item => (
                <li key={item.id}>
                  <button
                    type="button"
                    onClick={() => navigate(`/inbox?item=${item.id}`)}
                    className="w-full text-left px-3 py-2 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-800/60 transition-colors"
                  >
                    <div className="flex items-start gap-2">
                      <Mail className="w-4 h-4 text-slate-400 mt-0.5 shrink-0" />
                      <div className="min-w-0 flex-1">
                        <p className="text-sm font-medium text-slate-800 dark:text-slate-200 truncate">
                          {item.title}
                        </p>
                        <p className="text-xs text-slate-400">
                          {timeAgo(item.occurredAt, lang)}
                        </p>
                      </div>
                    </div>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>

      <ConfirmDialog
        open={deleteOpen}
        tone="danger"
        title={name}
        message={t('contacts.confirmDelete')}
        confirmLabel={t('common.delete')}
        loading={deleteMutation.isPending}
        onConfirm={() => deleteMutation.mutate()}
        onCancel={() => setDeleteOpen(false)}
      />
    </div>
  );
}

export function ContactDetailPanel({
  contactId,
  connectionId,
  readOnlyMode,
  onClose,
  onUpdated,
  onForbidden,
}: ContactDetailPanelProps) {
  const { t } = useI18n();
  const queryClient = useQueryClient();

  const { data: contact, isLoading, isError, refetch } = useQuery({
    queryKey: ['contact', contactId],
    queryFn: () => contactsApi.getContactById(contactId),
    enabled: !!contactId,
    staleTime: 0,
  });

  useEffect(() => {
    if (!contact) return;
    patchContactListCache(queryClient, contact);
  }, [contact, queryClient]);

  return (
    <div className="fixed inset-0 z-50 flex justify-end">
      <div onClick={onClose} className="absolute inset-0 bg-slate-900/40 dark:bg-black/50" />

      <div className="relative flex">
        {isLoading && (
          <div className="relative w-full max-w-[95vw] min-w-[420px] min-h-[50vh] bg-white dark:bg-slate-900 border-l border-slate-200 dark:border-slate-800 shadow-2xl flex flex-col items-center justify-center rounded-l-2xl text-slate-400">
            <Loader2 className="w-6 h-6 animate-spin" />
          </div>
        )}

        {isError && (
          <div className="relative w-full max-w-[95vw] min-w-[420px] min-h-[50vh] bg-white dark:bg-slate-900 border-l border-slate-200 dark:border-slate-800 shadow-2xl flex flex-col items-center justify-center gap-3 rounded-l-2xl text-slate-500 p-6">
            <AlertCircle className="w-8 h-8 text-rose-500" />
            <p className="text-sm">{t('contacts.loadFail')}</p>
            <button type="button" onClick={() => refetch()} className="text-sm text-brand-600 hover:underline">
              {t('common.retry')}
            </button>
          </div>
        )}

        {contact && !isLoading && (
          <ContactDetailBody
            key={`${contact.id}:${contact.etag ?? ''}`}
            contact={contact}
            contactId={contactId}
            connectionId={connectionId}
            readOnlyMode={readOnlyMode}
            onClose={onClose}
            onUpdated={onUpdated}
            onForbidden={onForbidden}
          />
        )}
      </div>
    </div>
  );
}
