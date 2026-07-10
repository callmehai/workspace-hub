import React, { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Users, Plus, Pencil, Trash2, ChevronLeft, ChevronRight,
  Search, AlertCircle, Loader2, X, Plug,
} from 'lucide-react';
import toast from 'react-hot-toast';
import { connectionsApi } from '../lib/connectionsApi';
import {
  contactsApi,
  type ContactDto,
  type GoogleContactSource,
} from '../lib/contactsApi';
import { handleContactMutateError, resolveContactApiError } from '../lib/contactsError';
import { Select } from '../components/Select';
import { PageSizeSelect } from '../components/PageSizeSelect';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { useI18n } from '../hooks/useI18n';
import { timeAgo } from '../lib/datetime';
import type { TranslationKey } from '../i18n/translations';

type SourceFilter = 'all' | GoogleContactSource;

const SOURCE_FILTERS: { value: SourceFilter; labelKey: TranslationKey }[] = [
  { value: 'all', labelKey: 'contacts.filterAll' },
  { value: 'Contact', labelKey: 'contacts.filterSaved' },
  { value: 'OtherContact', labelKey: 'contacts.filterOther' },
];

function buildPageNumbers(current: number, total: number): (number | '…')[] {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
  const pages: (number | '…')[] = [];
  const delta = 2;
  const left = current - delta;
  const right = current + delta;
  let prev: number | null = null;
  for (let p = 1; p <= total; p++) {
    if (p === 1 || p === total || (p >= left && p <= right)) {
      if (prev !== null && p - prev > 1) pages.push('…');
      pages.push(p);
      prev = p;
    }
  }
  return pages;
}

function FilterChip({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-[13px] font-medium border transition-colors whitespace-nowrap ${
        active
          ? 'bg-brand-50 text-brand-700 border-brand-200 dark:bg-brand-500/15 dark:text-brand-300 dark:border-brand-500/30'
          : 'bg-white text-slate-600 border-slate-200 hover:border-slate-300 dark:bg-slate-800 dark:text-slate-300 dark:border-slate-700 dark:hover:border-slate-600'
      }`}
    >
      {children}
    </button>
  );
}

interface ContactFormModalProps {
  open: boolean;
  contact?: ContactDto;
  connectionId: string;
  readOnlyMode: boolean;
  onClose: () => void;
  onForbidden: () => void;
}

function ContactFormModal({
  open, contact, connectionId, readOnlyMode, onClose, onForbidden,
}: ContactFormModalProps) {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const isEdit = !!contact;

  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');

  useEffect(() => {
    if (!open) return;
    setDisplayName(contact?.displayName ?? '');
    setEmail(contact?.email ?? '');
  }, [open, contact]);

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['contacts'] });
    queryClient.invalidateQueries({ queryKey: ['contact-suggest', connectionId] });
  };

  const errorOpts = {
    onConflict: () => {
      invalidate();
      onClose();
    },
    onForbidden: onForbidden,
  };

  const createMutation = useMutation({
    mutationFn: contactsApi.createContact,
    onSuccess: () => {
      toast.success(t('contacts.created'));
      invalidate();
      onClose();
    },
    onError: (err) => {
      handleContactMutateError(err, t, t('contacts.createFail'), errorOpts);
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: Parameters<typeof contactsApi.updateContact>[1] }) =>
      contactsApi.updateContact(id, payload),
    onSuccess: () => {
      toast.success(t('contacts.updated'));
      invalidate();
      onClose();
    },
    onError: (err) => {
      handleContactMutateError(err, t, t('contacts.updateFail'), errorOpts);
    },
  });

  if (!open) return null;

  const saving = createMutation.isPending || updateMutation.isPending;

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (readOnlyMode) return;
    if (!email.trim()) return toast.error(t('contacts.needEmail'));

    if (isEdit && contact) {
      if (!contact.etag) return toast.error(t('contacts.missingEtag'));
      updateMutation.mutate({
        id: contact.id,
        payload: {
          etag: contact.etag,
          email: email.trim(),
          displayName: displayName.trim() || undefined,
        },
      });
    } else {
      createMutation.mutate({
        connectionId,
        email: email.trim(),
        displayName: displayName.trim() || undefined,
      });
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/40 backdrop-blur-sm"
      onClick={(e) => { if (e.target === e.currentTarget && !saving) onClose(); }}
    >
      <div className="bg-white dark:bg-slate-900 rounded-2xl shadow-2xl w-full max-w-md overflow-hidden">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100 dark:border-slate-800">
          <h2 className="text-base font-semibold text-slate-900 dark:text-slate-100">
            {isEdit ? t('contacts.editTitle') : t('contacts.createTitle')}
          </h2>
          <button type="button" onClick={onClose} disabled={saving} className="p-1 rounded-md text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800">
            <X className="w-4 h-4" />
          </button>
        </div>
        <form onSubmit={handleSubmit} className="p-5 space-y-4">
          <div>
            <label className="block text-xs font-medium text-slate-500 dark:text-slate-400 mb-1">{t('contacts.name')}</label>
            <input
              type="text"
              readOnly={readOnlyMode}
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              className="w-full rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 px-3 py-2 text-sm"
              placeholder={t('contacts.namePlaceholder')}
            />
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-500 dark:text-slate-400 mb-1">{t('contacts.email')}</label>
            <input
              type="email"
              required
              readOnly={readOnlyMode}
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="w-full rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 px-3 py-2 text-sm"
              placeholder="name@example.com"
            />
          </div>
          <div className="flex justify-end gap-2 pt-2">
            <button type="button" onClick={onClose} disabled={saving} className="px-4 py-2 text-sm rounded-lg text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800">
              {t('common.cancel')}
            </button>
            <button
              type="submit"
              disabled={saving || readOnlyMode}
              className="px-4 py-2 text-sm rounded-lg bg-brand-600 text-white hover:bg-brand-700 disabled:opacity-50"
            >
              {saving ? t('common.saving') : t('common.save')}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

export const Contacts = () => {
  const { t, lang } = useI18n();
  const queryClient = useQueryClient();

  const [conn, setConn] = useState('');
  const [sourceFilter, setSourceFilter] = useState<SourceFilter>('all');
  const [searchInput, setSearchInput] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [page, setPage] = useState(1);
  const [limit, setLimit] = useState(20);
  const [readOnlyMode, setReadOnlyMode] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<ContactDto | undefined>();
  const [deleting, setDeleting] = useState<ContactDto | null>(null);

  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedSearch(searchInput.trim());
      setPage(1);
    }, 350);
    return () => clearTimeout(timer);
  }, [searchInput]);

  const { data: connections = [] } = useQuery({
    queryKey: ['connections'],
    queryFn: connectionsApi.getConnections,
  });

  const activeGmail = useMemo(
    () => connections.filter(c => c.serviceType.toLowerCase() === 'gmail' && c.status.toLowerCase() === 'active'),
    [connections],
  );
  const resolvedConn = conn || activeGmail[0]?.id || '';

  useEffect(() => {
    setReadOnlyMode(false);
  }, [resolvedConn]);

  const { data, isLoading, isError, error, refetch, isFetching } = useQuery({
    queryKey: ['contacts', resolvedConn, sourceFilter, debouncedSearch, page, limit],
    queryFn: () => contactsApi.getContacts(resolvedConn, {
      source: sourceFilter === 'all' ? undefined : sourceFilter,
      search: debouncedSearch || undefined,
      skip: (page - 1) * limit,
      top: limit,
    }),
    enabled: !!resolvedConn,
  });

  const items = data?.value ?? [];
  const total = data?.['@odata.count'] ?? 0;

  const deleteMutation = useMutation({
    mutationFn: contactsApi.deleteContact,
    onSuccess: () => {
      toast.success(t('contacts.deleted'));
      queryClient.invalidateQueries({ queryKey: ['contacts'] });
      if (resolvedConn) {
        queryClient.invalidateQueries({ queryKey: ['contact-suggest', resolvedConn] });
      }
      setDeleting(null);
    },
    onError: (err) => {
      handleContactMutateError(err, t, t('contacts.deleteFail'), {
        onForbidden: () => setReadOnlyMode(true),
      });
    },
  });

  const totalPages = Math.max(1, Math.ceil(total / limit));
  const rangeStart = total === 0 ? 0 : (page - 1) * limit + 1;
  const rangeEnd = Math.min(page * limit, total);
  const pageNumbers = buildPageNumbers(page, totalPages);

  const openCreate = () => {
    setEditing(undefined);
    setModalOpen(true);
  };

  const openEdit = (contact: ContactDto) => {
    setEditing(contact);
    setModalOpen(true);
  };

  const formatTime = (iso?: string | null) => {
    if (!iso) return '—';
    return timeAgo(iso, lang);
  };

  return (
    <div className="flex flex-col h-full min-h-0">
      <div className="px-4 sm:px-6 pt-5 pb-4 border-b border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900">
        <div className="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-3">
          <div>
            <h1 className="text-xl font-semibold text-slate-900 dark:text-slate-100 flex items-center gap-2">
              <Users className="w-5 h-5 text-brand-600" />
              {t('contacts.title')}
            </h1>
            <p className="text-sm text-slate-500 dark:text-slate-400 mt-1">{t('contacts.subtitle')}</p>
          </div>
          {!readOnlyMode && resolvedConn && (
            <button
              type="button"
              onClick={openCreate}
              className="inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-brand-600 text-white text-sm font-medium hover:bg-brand-700"
            >
              <Plus className="w-4 h-4" />
              {t('contacts.add')}
            </button>
          )}
        </div>

        {readOnlyMode && (
          <div className="mt-4 flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2.5 text-sm text-amber-800 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-200">
            <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" />
            <span>
              {t('contacts.readOnlyBanner')}{' '}
              <Link to="/integrations" className="font-medium underline hover:no-underline">
                {t('nav.integrations')}
              </Link>
            </span>
          </div>
        )}

        <div className="mt-4 flex flex-col sm:flex-row sm:items-center gap-2">
          <div className="w-full sm:w-64 shrink-0">
            <Select
              value={resolvedConn}
              onChange={setConn}
              placeholder={t('sendEmail.connectionPlaceholder')}
              options={activeGmail.map(c => ({
                value: c.id,
                label: `Gmail · ${c.providerAccountId}`,
              }))}
              className="h-9 text-[13px]"
            />
          </div>
          <div className="relative flex-1 min-w-0">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
            <input
              type="search"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              placeholder={t('contacts.searchPlaceholder')}
              className="w-full h-9 pl-9 pr-4 rounded-lg border border-slate-200 bg-white text-[13px] text-slate-900 placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-brand-500/30 focus:border-brand-400 transition dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100 dark:placeholder:text-slate-500"
            />
          </div>
        </div>

        <div className="mt-3 flex flex-wrap gap-2 items-center">
          {SOURCE_FILTERS.map(({ value, labelKey }) => (
            <FilterChip
              key={value}
              active={sourceFilter === value}
              onClick={() => { setSourceFilter(value); setPage(1); }}
            >
              {t(labelKey)}
            </FilterChip>
          ))}
        </div>
      </div>

      <div className="flex-1 min-h-0 overflow-auto px-4 sm:px-6 py-4">
        {!resolvedConn && (
          <div className="flex flex-col items-center justify-center py-16 text-center text-slate-500 dark:text-slate-400">
            <Plug className="w-10 h-10 mb-3 opacity-50" />
            <p className="text-sm">{t('sendEmail.needConn')}</p>
            <Link to="/integrations" className="mt-3 text-sm text-brand-600 hover:underline">
              {t('nav.integrations')}
            </Link>
          </div>
        )}

        {resolvedConn && isLoading && (
          <div className="flex items-center justify-center py-16 text-slate-400">
            <Loader2 className="w-6 h-6 animate-spin" />
          </div>
        )}

        {resolvedConn && isError && (
          <div className="flex flex-col items-center py-16 gap-3 text-slate-500 text-sm text-center px-4">
            <AlertCircle className="w-8 h-8 text-rose-500" />
            <p>{resolveContactApiError(error, t) ?? t('contacts.loadFail')}</p>
            <button type="button" onClick={() => refetch()} className="text-sm text-brand-600 hover:underline">
              {t('common.retry')}
            </button>
          </div>
        )}

        {resolvedConn && data && !isLoading && (
          <>
            {items.length === 0 ? (
              <p className="text-center text-sm text-slate-500 py-12">{t('contacts.empty')}</p>
            ) : (
              <div className="overflow-x-auto rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-slate-100 dark:border-slate-800 text-left text-xs uppercase tracking-wide text-slate-400">
                      <th className="px-4 py-3 font-medium">{t('contacts.name')}</th>
                      <th className="px-4 py-3 font-medium">{t('contacts.email')}</th>
                      <th className="px-4 py-3 font-medium">{t('contacts.source')}</th>
                      <th className="px-4 py-3 font-medium">{t('contacts.colUpdated')}</th>
                      <th className="px-4 py-3 font-medium w-24" />
                    </tr>
                  </thead>
                  <tbody>
                    {items.map((row) => {
                      const mutable = row.source === 'Contact' && !readOnlyMode;
                      return (
                        <tr key={row.id} className="border-b border-slate-50 dark:border-slate-800/80 last:border-0 hover:bg-slate-50/80 dark:hover:bg-slate-800/40">
                          <td className="px-4 py-3 font-medium text-slate-900 dark:text-slate-100">
                            {row.displayName || '—'}
                          </td>
                          <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{row.email}</td>
                          <td className="px-4 py-3">
                            <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${
                              row.source === 'Contact'
                                ? 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-300'
                                : 'bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-400'
                            }`}>
                              {row.source === 'Contact' ? t('contacts.badgeSaved') : t('contacts.badgeOther')}
                            </span>
                          </td>
                          <td className="px-4 py-3 text-slate-500 dark:text-slate-400 tabular-nums">
                            {formatTime(row.updatedAt ?? row.syncedAt)}
                            {isFetching && <Loader2 className="inline w-3 h-3 ml-1 animate-spin opacity-50" />}
                          </td>
                          <td className="px-4 py-3">
                            {row.source === 'Contact' ? (
                              <div className="flex items-center gap-1 justify-end">
                                <button
                                  type="button"
                                  title={mutable ? t('common.edit') : t('contacts.readOnlyTooltip')}
                                  disabled={!mutable}
                                  onClick={() => openEdit(row)}
                                  className="p-1.5 rounded-md text-slate-400 hover:text-brand-600 hover:bg-brand-50 disabled:opacity-40 disabled:cursor-not-allowed dark:hover:bg-brand-500/10"
                                >
                                  <Pencil className="w-4 h-4" />
                                </button>
                                <button
                                  type="button"
                                  title={mutable ? t('common.delete') : t('contacts.readOnlyTooltip')}
                                  disabled={!mutable}
                                  onClick={() => setDeleting(row)}
                                  className="p-1.5 rounded-md text-slate-400 hover:text-rose-600 hover:bg-rose-50 disabled:opacity-40 disabled:cursor-not-allowed dark:hover:bg-rose-500/10"
                                >
                                  <Trash2 className="w-4 h-4" />
                                </button>
                              </div>
                            ) : (
                              <span className="text-xs text-slate-400" title={t('contacts.readOnlyTooltip')}>
                                {t('contacts.readOnlyShort')}
                              </span>
                            )}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}

            {total > 0 && (
              <div className="flex items-center justify-between mt-4 flex-wrap gap-3">
                <div className="flex items-center gap-3">
                  <PageSizeSelect value={limit} onChange={(v) => { setLimit(v); setPage(1); }} />
                  <span className="text-[12.5px] text-slate-400 dark:text-slate-500">
                    {t('inbox.range', { start: rangeStart, end: rangeEnd, total })}
                  </span>
                </div>

                {totalPages > 1 && (
                  <div className="flex items-center gap-1">
                    <button
                      type="button"
                      onClick={() => setPage(p => Math.max(1, p - 1))}
                      disabled={page === 1}
                      aria-label={t('common.prevPage')}
                      className="h-8 w-8 flex items-center justify-center rounded-lg border border-slate-200 bg-white text-slate-600 hover:bg-slate-50 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-300 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
                    >
                      <ChevronLeft className="w-4 h-4" />
                    </button>

                    {pageNumbers.map((p, i) =>
                      p === '…' ? (
                        <span key={`ellipsis-${i}`} className="h-8 w-8 flex items-center justify-center text-[13px] text-slate-400 dark:text-slate-500">…</span>
                      ) : (
                        <button
                          key={p}
                          type="button"
                          onClick={() => setPage(p)}
                          className={`h-8 w-8 flex items-center justify-center rounded-lg text-[13px] font-medium border transition-colors ${
                            p === page
                              ? 'bg-brand-600 text-white border-brand-600'
                              : 'bg-white text-slate-700 border-slate-200 hover:bg-slate-50 dark:bg-slate-800 dark:text-slate-300 dark:border-slate-700 dark:hover:bg-slate-700'
                          }`}
                        >
                          {p}
                        </button>
                      ),
                    )}

                    <button
                      type="button"
                      onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                      disabled={page === totalPages}
                      aria-label={t('common.nextPage')}
                      className="h-8 w-8 flex items-center justify-center rounded-lg border border-slate-200 bg-white text-slate-600 hover:bg-slate-50 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-300 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
                    >
                      <ChevronRight className="w-4 h-4" />
                    </button>
                  </div>
                )}
              </div>
            )}
          </>
        )}
      </div>

      <ContactFormModal
        open={modalOpen}
        contact={editing}
        connectionId={resolvedConn}
        readOnlyMode={readOnlyMode}
        onClose={() => setModalOpen(false)}
        onForbidden={() => setReadOnlyMode(true)}
      />

      <ConfirmDialog
        open={deleting !== null}
        tone="danger"
        title={deleting?.displayName || deleting?.email}
        message={t('contacts.confirmDelete')}
        confirmLabel={t('common.delete')}
        loading={deleteMutation.isPending}
        onConfirm={() => {
          if (deleting) deleteMutation.mutate(deleting.id);
        }}
        onCancel={() => setDeleting(null)}
      />
    </div>
  );
};
