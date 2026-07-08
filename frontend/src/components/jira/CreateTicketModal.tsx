import { useState, useCallback, useRef, useEffect } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { Loader2, X, ChevronDown, Ticket } from 'lucide-react';
import toast from 'react-hot-toast';
import { itemsApi } from '../../lib/itemsApi';
import { jiraApi, type JiraUser } from '../../lib/jiraApi';
import { connectionsApi, type ConnectionDto } from '../../lib/connectionsApi';
import { handleApiError } from '../../lib/errorUtils';
import { useI18n } from '../../hooks/useI18n';

interface Props {
  isOpen: boolean;
  onClose: () => void;
}

const INPUT_CLS =
  'w-full bg-white dark:bg-slate-800 border border-slate-300 dark:border-slate-700 rounded-lg py-2 px-3 text-[13px] text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500 transition-shadow';
const SELECT_CLS = INPUT_CLS + ' appearance-none cursor-pointer';
const LABEL_CLS = 'block text-[13px] font-medium text-slate-700 dark:text-slate-200 mb-1.5';

/** Validates a single label — no whitespace allowed (mirrors CreateTicketRequestValidator.cs) */
function labelHasSpace(label: string) {
  return /\s/.test(label);
}

export const CreateTicketModal = ({ isOpen, onClose }: Props) => {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { t } = useI18n();

  // ── Form state ────────────────────────────────────────────────────────────
  const [connectionId, setConnectionId] = useState('');
  const [projectKey, setProjectKey] = useState('');
  const [issueType, setIssueType] = useState('');
  const [summary, setSummary] = useState('');
  const [description, setDescription] = useState('');
  const [priority, setPriority] = useState('');
  const [labels, setLabels] = useState<string[]>([]);
  const [labelInput, setLabelInput] = useState('');
  const [labelError, setLabelError] = useState('');
  const [assigneeAccountId, setAssigneeAccountId] = useState('');
  const [assigneeQuery, setAssigneeQuery] = useState('');
  const [assigneeDropOpen, setAssigneeDropOpen] = useState(false);
  const assigneeRef = useRef<HTMLDivElement>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [debouncedAssigneeQuery, setDebouncedAssigneeQuery] = useState('');

  // Reset form when modal is closed
  /* eslint-disable react-hooks/set-state-in-effect */
  useEffect(() => {
    if (!isOpen) {
      setConnectionId('');
      setProjectKey('');
      setIssueType('');
      setSummary('');
      setDescription('');
      setPriority('');
      setLabels([]);
      setLabelInput('');
      setLabelError('');
      setAssigneeAccountId('');
      setAssigneeQuery('');
      setDebouncedAssigneeQuery('');
    }
  }, [isOpen]);
  /* eslint-enable react-hooks/set-state-in-effect */

  // Close assignee dropdown when clicking outside
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

  // ── Remote data ────────────────────────────────────────────────────────────
  const { data: connections = [] } = useQuery({
    queryKey: ['connections'],
    queryFn: connectionsApi.getConnections,
    enabled: isOpen,
  });

  const jiraConnections: ConnectionDto[] = connections.filter(
    (c) => c.serviceType.toLowerCase() === 'jira' && c.status.toLowerCase() === 'active',
  );

  const { data: projects = [], isLoading: loadingProjects } = useQuery({
    queryKey: ['jira', 'projects', connectionId],
    queryFn: () => jiraApi.getProjects(connectionId),
    enabled: !!connectionId,
    staleTime: 5 * 60_000,
  });

  const { data: issueTypes = [] } = useQuery({
    queryKey: ['jira', 'issueTypes', connectionId, projectKey],
    queryFn: () => jiraApi.getIssueTypes(connectionId, projectKey),
    enabled: !!connectionId && !!projectKey,
    staleTime: 5 * 60_000,
  });

  const { data: priorities = [] } = useQuery({
    queryKey: ['jira', 'priorities', connectionId],
    queryFn: () => jiraApi.getPriorities(connectionId),
    enabled: !!connectionId,
    staleTime: 5 * 60_000,
  });

  const { data: assignableUsers = [], isFetching: loadingAssignees } = useQuery({
    queryKey: ['jira', 'assignableUsers', connectionId, projectKey, debouncedAssigneeQuery],
    queryFn: () => jiraApi.getAssignableUsers(connectionId, projectKey, debouncedAssigneeQuery),
    enabled: !!connectionId && !!projectKey && assigneeDropOpen,
    staleTime: 0,
  });

  // Debounce assignee search
  const handleAssigneeQueryChange = useCallback((val: string) => {
    setAssigneeQuery(val);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => setDebouncedAssigneeQuery(val), 300);
  }, []);

  // ── Labels input ───────────────────────────────────────────────────────────
  const addLabel = () => {
    const trimmed = labelInput.trim();
    if (!trimmed) return;
    if (labelHasSpace(trimmed)) {
      setLabelError(t('createTicket.labelsNoSpaces'));
      return;
    }
    if (!labels.includes(trimmed)) setLabels((prev) => [...prev, trimmed]);
    setLabelInput('');
    setLabelError('');
  };

  const removeLabel = (label: string) => setLabels((prev) => prev.filter((l) => l !== label));

  // ── Mutation ───────────────────────────────────────────────────────────────
  const createTicket = useMutation({
    mutationFn: () =>
      itemsApi.createTicket({
        connectionId,
        projectKey,
        issueType,
        summary: summary.trim(),
        description: description.trim() || undefined,
        assignee: assigneeAccountId || undefined,
        priority: priority || undefined,
        labels: labels.length > 0 ? labels : undefined,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['items'] });
      toast.success(t('createTicket.created'));
      onClose();
    },
    onError: (err) => handleApiError(err, t('createTicket.createFail'), { navigate }),
  });

  // ── Validation ─────────────────────────────────────────────────────────────
  const handleSubmit = () => {
    if (!connectionId) { toast.error(t('createTicket.needConnection')); return; }
    if (!projectKey) { toast.error(t('createTicket.needProject')); return; }
    if (!issueType) { toast.error(t('createTicket.needIssueType')); return; }
    const s = summary.trim();
    if (!s) { toast.error(t('createTicket.needSummary')); return; }
    if (s.length > 255) { toast.error(t('createTicket.summaryMax')); return; }
    if (labels.some(labelHasSpace)) { toast.error(t('createTicket.labelsNoSpaces')); return; }
    createTicket.mutate();
  };

  if (!isOpen) return null;

  const selectedAssignee: JiraUser | undefined = assignableUsers.find(
    (u) => u.accountId === assigneeAccountId,
  );

  return (
    <div className="fixed inset-0 bg-slate-900/50 backdrop-blur-sm flex items-center justify-center p-4 z-50">
      <div className="bg-white dark:bg-slate-900 rounded-xl w-full max-w-xl shadow-2xl overflow-hidden flex flex-col max-h-[90vh]">

        {/* Header */}
        <div className="px-5 py-4 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between shrink-0">
          <div className="flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-lg bg-violet-50 dark:bg-violet-500/10 text-violet-600 dark:text-violet-400 flex items-center justify-center">
              <Ticket className="w-4 h-4" />
            </div>
            <h2 className="text-[16px] font-semibold text-slate-900 dark:text-slate-100">
              {t('createTicket.title')}
            </h2>
          </div>
          <button
            onClick={onClose}
            className="p-1.5 text-slate-400 hover:text-slate-600 dark:hover:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 rounded-lg transition-colors"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto p-5 space-y-4">

          {/* No Jira connection warning */}
          {jiraConnections.length === 0 && (
            <div className="rounded-lg bg-amber-50 dark:bg-amber-500/10 border border-amber-200 dark:border-amber-500/20 px-3 py-2.5 text-[13px] text-amber-800 dark:text-amber-300">
              {t('createTicket.noConn')}
            </div>
          )}

          {/* Jira connection */}
          <div>
            <label className={LABEL_CLS}>{t('createTicket.account')}</label>
            <div className="relative">
              <select
                id="createTicket-connection"
                value={connectionId}
                onChange={(e) => { setConnectionId(e.target.value); setProjectKey(''); setIssueType(''); }}
                className={SELECT_CLS}
                disabled={jiraConnections.length === 0}
              >
                <option value="">{t('createTicket.selectAccount')}</option>
                {jiraConnections.map((c) => (
                  <option key={c.id} value={c.id}>{c.providerAccountId || c.id}</option>
                ))}
              </select>
              <ChevronDown className="absolute right-2.5 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400 pointer-events-none" />
            </div>
          </div>

          {/* Project */}
          {connectionId && (
            <div>
              <label className={LABEL_CLS}>{t('createTicket.project')}</label>
              <div className="relative">
                {loadingProjects ? (
                  <div className="flex items-center gap-2 text-[13px] text-slate-500 dark:text-slate-400 py-2">
                    <Loader2 className="w-4 h-4 animate-spin" />
                    {t('createTicket.loadingProjects')}
                  </div>
                ) : (
                  <>
                    <select
                      id="createTicket-project"
                      value={projectKey}
                      onChange={(e) => { setProjectKey(e.target.value); setIssueType(''); setAssigneeAccountId(''); setAssigneeQuery(''); }}
                      className={SELECT_CLS}
                    >
                      <option value="">{t('createTicket.selectProject')}</option>
                      {projects.map((p) => (
                        <option key={p.key} value={p.key}>{p.name} ({p.key})</option>
                      ))}
                    </select>
                    <ChevronDown className="absolute right-2.5 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400 pointer-events-none" />
                  </>
                )}
              </div>
            </div>
          )}

          {/* Issue type */}
          {projectKey && (
            <div>
              <label className={LABEL_CLS}>{t('createTicket.issueType')}</label>
              <div className="relative">
                <select
                  id="createTicket-issueType"
                  value={issueType}
                  onChange={(e) => setIssueType(e.target.value)}
                  className={SELECT_CLS}
                >
                  <option value="">{t('createTicket.selectIssueType')}</option>
                  {issueTypes.filter((it) => !it.subtask).map((it) => (
                    <option key={it.id} value={it.name}>{it.name}</option>
                  ))}
                </select>
                <ChevronDown className="absolute right-2.5 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400 pointer-events-none" />
              </div>
            </div>
          )}

          {/* Summary */}
          <div>
            <label className={LABEL_CLS}>
              {t('createTicket.summary')}
              <span className="ml-1 text-[11px] text-slate-400 font-normal">({summary.length}/255)</span>
            </label>
            <input
              id="createTicket-summary"
              type="text"
              value={summary}
              maxLength={255}
              onChange={(e) => setSummary(e.target.value)}
              placeholder={t('createTicket.summaryPlaceholder')}
              className={INPUT_CLS}
            />
          </div>

          {/* Description */}
          <div>
            <label className={LABEL_CLS}>{t('createTicket.description')}</label>
            <textarea
              id="createTicket-description"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder={t('createTicket.descPlaceholder')}
              rows={3}
              className={INPUT_CLS + ' resize-none'}
            />
          </div>

          {/* Priority + Assignee side by side */}
          <div className="grid grid-cols-2 gap-3">
            {/* Priority */}
            <div>
              <label className={LABEL_CLS}>{t('createTicket.priority')}</label>
              <div className="relative">
                <select
                  id="createTicket-priority"
                  value={priority}
                  onChange={(e) => setPriority(e.target.value)}
                  className={SELECT_CLS}
                  disabled={!connectionId}
                >
                  <option value="">{t('createTicket.selectPriority')}</option>
                  {priorities.map((p) => (
                    <option key={p.id} value={p.name}>{p.name}</option>
                  ))}
                </select>
                <ChevronDown className="absolute right-2.5 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400 pointer-events-none" />
              </div>
            </div>

            {/* Assignee */}
            <div>
              <label className={LABEL_CLS}>{t('createTicket.assignee')}</label>
              <div className="relative" ref={assigneeRef}>
                <input
                  id="createTicket-assignee"
                  type="text"
                  value={assigneeQuery}
                  onChange={(e) => { handleAssigneeQueryChange(e.target.value); setAssigneeDropOpen(true); }}
                  onFocus={() => { if (projectKey) setAssigneeDropOpen(true); }}
                  placeholder={
                    selectedAssignee
                      ? selectedAssignee.displayName
                      : t('createTicket.assigneePlaceholder')
                  }
                  disabled={!projectKey}
                  className={INPUT_CLS}
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
                          onClick={() => { setAssigneeAccountId(''); setAssigneeQuery(''); setAssigneeDropOpen(false); }}
                          className="w-full text-left px-3 py-1.5 text-[12.5px] text-slate-500 hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors"
                        >
                          {t('createTicket.noAssignee')}
                        </button>
                        {assignableUsers.map((u) => (
                          <button
                            key={u.accountId}
                            onClick={() => { setAssigneeAccountId(u.accountId); setAssigneeQuery(u.displayName); setAssigneeDropOpen(false); }}
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
          </div>

          {/* Labels */}
          <div>
            <label className={LABEL_CLS}>{t('createTicket.labels')}</label>
            <div className="flex flex-wrap gap-1.5 mb-2">
              {labels.map((l) => (
                <span
                  key={l}
                  className="inline-flex items-center gap-1 px-2 py-0.5 rounded bg-violet-50 dark:bg-violet-500/10 text-violet-700 dark:text-violet-300 text-[11.5px] font-medium border border-violet-100 dark:border-violet-500/20"
                >
                  {l}
                  <button onClick={() => removeLabel(l)} className="hover:text-violet-900 dark:hover:text-violet-100 transition-colors">
                    <X className="w-3 h-3" />
                  </button>
                </span>
              ))}
            </div>
            <div className="flex gap-2">
              <input
                id="createTicket-labelInput"
                type="text"
                value={labelInput}
                onChange={(e) => { setLabelInput(e.target.value); setLabelError(''); }}
                onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); addLabel(); } }}
                placeholder={t('createTicket.labelsHint')}
                className={INPUT_CLS}
              />
              <button
                onClick={addLabel}
                className="px-3 py-2 rounded-lg bg-slate-100 dark:bg-slate-700 text-slate-600 dark:text-slate-300 text-[13px] font-medium hover:bg-slate-200 dark:hover:bg-slate-600 transition-colors whitespace-nowrap"
              >
                +
              </button>
            </div>
            {labelError && (
              <p className="mt-1 text-[12px] text-rose-600 dark:text-rose-400">{labelError}</p>
            )}
          </div>
        </div>

        {/* Footer */}
        <div className="px-5 py-4 border-t border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-800/50 flex justify-end gap-2 shrink-0">
          <button
            onClick={onClose}
            className="px-4 py-2 text-[13px] font-medium text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100 hover:bg-slate-200 dark:hover:bg-slate-700 rounded-lg transition-colors"
          >
            {t('common.cancel')}
          </button>
          <button
            id="createTicket-submit"
            onClick={handleSubmit}
            disabled={jiraConnections.length === 0 || createTicket.isPending}
            className="bg-violet-600 hover:bg-violet-700 disabled:opacity-50 text-white px-4 py-2 rounded-lg text-[13px] font-semibold transition-colors inline-flex items-center gap-2"
          >
            {createTicket.isPending && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
            {createTicket.isPending ? t('createTicket.creating') : t('createTicket.create')}
          </button>
        </div>
      </div>
    </div>
  );
};
