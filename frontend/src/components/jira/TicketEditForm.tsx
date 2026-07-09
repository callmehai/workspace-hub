/**
 * TicketEditForm — inline edit panel inside ItemDetail drawer for Jira tickets (SCRUM-57).
 * Renders when `isEditing && item.type === 'Ticket'`.
 * Fields: summary, description, priority (dropdown from Jira), assignee (text input for accountId or displayName),
 *         labels (comma-separated, no spaces), status transition (dropdown), comment.
 */
import { useQuery } from '@tanstack/react-query';
import { Loader2 } from 'lucide-react';
import { useState, useRef, useEffect, useCallback } from 'react';
import { jiraApi, type JiraUser } from '../../lib/jiraApi';
import type { TranslationKey } from '../../i18n/translations';

type TFn = (key: TranslationKey, vars?: Record<string, string | number>) => string;

export interface TicketFormState {
  summary: string;
  description: string;
  priority: string;
  assigneeAccountId: string;
  assigneeQuery: string;
  labelsRaw: string;       // comma-separated; validated for no-whitespace on save
  statusTransitionId: string;
  comment: string;
}

interface Props {
  itemId: string;
  connectionId: string;
  projectKey: string;
  form: TicketFormState;
  onChange: (form: TicketFormState) => void;
  onSave: () => void;
  onCancel: () => void;
  isPending: boolean;
  t: TFn;
}

const INPUT = 'w-full bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-lg px-3 py-2 text-[13px] text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 focus:outline-none focus:border-brand-500 dark:focus:border-brand-400 transition-colors';
const LABEL = 'block text-[11.5px] font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wide mb-1';

export const TicketEditForm = ({
  itemId, connectionId, projectKey, form, onChange, onSave, onCancel, isPending, t,
}: Props) => {
  const set = (key: keyof TicketFormState) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) =>
    onChange({ ...form, [key]: e.target.value });

  const [assigneeDropOpen, setAssigneeDropOpen] = useState(false);
  const assigneeRef = useRef<HTMLDivElement>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [debouncedAssigneeQuery, setDebouncedAssigneeQuery] = useState(form.assigneeQuery);

  useEffect(() => {
    if (!assigneeDropOpen) return;
    const handler = (e: MouseEvent) => {
      if (assigneeRef.current && !assigneeRef.current.contains(e.target as Node)) {
        setAssigneeDropOpen(false);
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [assigneeDropOpen]);

  const handleAssigneeQueryChange = useCallback((val: string) => {
    onChange({ ...form, assigneeQuery: val });
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => setDebouncedAssigneeQuery(val), 300);
  }, [form, onChange]);


  // ── Remote data ────────────────────────────────────────────────────────────
  const { data: priorities = [] } = useQuery({
    queryKey: ['jira', 'priorities', connectionId],
    queryFn: () => jiraApi.getPriorities(connectionId),
    enabled: !!connectionId,
    staleTime: 5 * 60_000,
  });

  const { data: transitions = [], isLoading: loadingTransitions } = useQuery({
    queryKey: ['jira', 'transitions', connectionId, itemId],
    queryFn: () => jiraApi.getTransitions(connectionId, itemId),
    enabled: !!connectionId && !!itemId,
    staleTime: 0,
  });

  const { data: assignableUsers = [], isFetching: loadingAssignees } = useQuery({
    queryKey: ['jira', 'assignableUsers', connectionId, projectKey, debouncedAssigneeQuery],
    queryFn: () => jiraApi.getAssignableUsers(connectionId, projectKey, debouncedAssigneeQuery),
    enabled: !!connectionId && !!projectKey && assigneeDropOpen,
    staleTime: 0,
  });

  const selectedAssignee: JiraUser | undefined = assignableUsers.find(
    (u) => u.accountId === form.assigneeAccountId,
  );

  return (
    <div className="border border-violet-200 dark:border-violet-500/30 rounded-[10px] p-4 bg-violet-50/30 dark:bg-violet-500/5 space-y-4 mb-[18px]">
      <h3 className="text-[11px] font-bold text-violet-600 dark:text-violet-400 uppercase tracking-wider">
        {t('ticket.editTitle')}
      </h3>

      {/* Summary */}
      <div>
        <label className={LABEL}>
          {t('ticket.summaryLabel')}
          <span className="ml-1 text-[10px] font-normal text-slate-400">({form.summary.length}/255)</span>
        </label>
        <input
          type="text"
          value={form.summary}
          maxLength={255}
          onChange={set('summary')}
          className={INPUT}
        />
      </div>

      {/* Description */}
      <div>
        <label className={LABEL}>{t('ticket.descLabel')}</label>
        <textarea
          value={form.description}
          onChange={set('description')}
          rows={3}
          className={INPUT + ' resize-none'}
        />
      </div>

      {/* Priority + Status transition side by side */}
      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className={LABEL}>{t('ticket.priorityLabel')}</label>
          <select
            value={form.priority}
            onChange={set('priority')}
            className={INPUT + ' appearance-none cursor-pointer'}
          >
            <option value="">{t('createTicket.selectPriority')}</option>
            {priorities.map((p) => (
              <option key={p.id} value={p.name}>{p.name}</option>
            ))}
          </select>
        </div>
        <div>
          <label className={LABEL}>{t('ticket.transitionLabel')}</label>
          {loadingTransitions ? (
            <div className="flex items-center gap-2 text-[13px] text-slate-500 py-2">
              <Loader2 className="w-3.5 h-3.5 animate-spin" />{t('ticket.loadingTransitions')}
            </div>
          ) : transitions.length === 0 ? (
            <p className="text-[12px] text-slate-400 py-2">{t('ticket.noTransitions')}</p>
          ) : (
            <select
              value={form.statusTransitionId}
              onChange={set('statusTransitionId')}
              className={INPUT + ' appearance-none cursor-pointer'}
            >
              <option value="">{t('ticket.selectTransition')}</option>
              {transitions.map((tr) => (
                <option key={tr.id} value={tr.id}>{tr.name}</option>
              ))}
            </select>
          )}
        </div>
      </div>

      {/* Assignee — search dropdown */}
      <div>
        <label className={LABEL}>{t('ticket.assigneeLabel')}</label>
        <div className="relative" ref={assigneeRef}>
          <input
            type="text"
            value={form.assigneeQuery}
            onChange={(e) => { handleAssigneeQueryChange(e.target.value); setAssigneeDropOpen(true); }}
            onFocus={() => { if (projectKey) setAssigneeDropOpen(true); }}
            placeholder={selectedAssignee ? selectedAssignee.displayName : t('createTicket.assigneePlaceholder')}
            className={INPUT}
          />
          {assigneeDropOpen && projectKey && (
            <div className="absolute top-full left-0 mt-1 w-full bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 shadow-xl rounded-lg py-1 z-[70] max-h-48 overflow-y-auto">
              {loadingAssignees ? (
                <div className="flex items-center gap-2 px-3 py-2 text-[12px] text-slate-500">
                  <Loader2 className="w-3.5 h-3.5 animate-spin" />{t('createTicket.assigneePlaceholder')}
                </div>
              ) : assignableUsers.length === 0 ? (
                <div className="px-3 py-2 text-[12px] text-slate-400">{t('common.noOptions')}</div>
              ) : (
                <>
                  <button
                    onClick={() => { onChange({ ...form, assigneeAccountId: '', assigneeQuery: '' }); setAssigneeDropOpen(false); }}
                    className="w-full text-left px-3 py-1.5 text-[12.5px] text-slate-500 hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors"
                  >
                    {t('createTicket.noAssignee')}
                  </button>
                  {assignableUsers.map((u) => (
                    <button
                      key={u.accountId}
                      onClick={() => { onChange({ ...form, assigneeAccountId: u.accountId, assigneeQuery: u.displayName }); setAssigneeDropOpen(false); }}
                      className="w-full text-left px-3 py-1.5 text-[12.5px] text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors"
                    >
                      {u.displayName}
                      {u.email && <span className="ml-1 text-slate-400 text-[11px]">({u.email})</span>}
                    </button>
                  ))}
                </>
              )}
            </div>
          )}
        </div>
      </div>

      {/* Labels — comma-separated, validated on save for no whitespace */}
      <div>
        <label className={LABEL}>{t('ticket.labelsLabel')}</label>
        <input
          type="text"
          value={form.labelsRaw}
          onChange={set('labelsRaw')}
          placeholder={t('ticket.labelsPlaceholder')}
          className={INPUT}
        />
      </div>

      {/* Comment */}
      <div>
        <label className={LABEL}>{t('ticket.comment')}</label>
        <textarea
          value={form.comment}
          onChange={set('comment')}
          placeholder={t('ticket.commentPlaceholder')}
          rows={2}
          className={INPUT + ' resize-none'}
        />
      </div>

      {/* Actions */}
      <div className="flex justify-end gap-2 pt-2">
        <button
          onClick={onCancel}
          className="px-3.5 py-2 border border-slate-200 dark:border-slate-700 rounded-lg text-xs font-medium text-slate-600 dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-700 transition-colors"
        >
          {t('common.cancel')}
        </button>
        <button
          onClick={onSave}
          disabled={isPending}
          className="px-3.5 py-2 bg-violet-600 text-white rounded-lg text-xs font-semibold hover:bg-violet-700 transition-colors flex items-center gap-1.5 disabled:opacity-60"
        >
          {isPending && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
          <span>{t('common.save')}</span>
        </button>
      </div>
    </div>
  );
};
