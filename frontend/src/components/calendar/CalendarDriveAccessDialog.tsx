import { useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { HelpCircle, Loader2, UserCircle } from 'lucide-react';
import { useI18n } from '../../hooks/useI18n';
import { Select } from '../Select';
import type { DrivePermissionRole } from '../../types/drive';

export type DriveAccessChoice = 'people' | 'link' | 'none';

export interface DriveAccessFileNeed {
  itemId: string;
  title: string;
  missingEmails: string[];
  uploadedThisSession: boolean;
}

interface CalendarDriveAccessDialogProps {
  open: boolean;
  files: DriveAccessFileNeed[];
  guests: string[];
  saving?: boolean;
  onCancel: () => void;
  /** choice = cách chia sẻ; role chỉ áp dụng khi people/link (none bỏ qua). */
  onSave: (choice: DriveAccessChoice, role: DrivePermissionRole) => void;
}

const ROLE_OPTIONS: DrivePermissionRole[] = ['reader', 'commenter', 'writer'];

function uniqueEmails(values: string[]) {
  return Array.from(new Set(values.map(value => value.trim()).filter(Boolean)));
}

function roleLabelKey(role: DrivePermissionRole) {
  if (role === 'commenter') return 'drive.share.roleCommenter' as const;
  if (role === 'writer') return 'drive.share.roleWriter' as const;
  return 'drive.share.roleReader' as const;
}

/**
 * Dialog hỏi quyền Drive khi lưu event có đính kèm.
 * Des đồng bộ ConfirmDialog / CalendarGuestNotificationDialog (brand, nút rounded-lg, light/dark).
 *
 * Wrapper chỉ mount content khi open → state form tự reset mỗi lần mở (không cần effect sync).
 */
export function CalendarDriveAccessDialog(props: CalendarDriveAccessDialogProps) {
  if (!props.open) return null;
  return <CalendarDriveAccessDialogContent {...props} />;
}

function CalendarDriveAccessDialogContent({
  files,
  guests,
  saving = false,
  onCancel,
  onSave,
}: CalendarDriveAccessDialogProps) {
  const { t, lang } = useI18n();
  const [choice, setChoice] = useState<DriveAccessChoice>('people');
  const [role, setRole] = useState<DrivePermissionRole>('reader');
  const [roleSelectOpen, setRoleSelectOpen] = useState<'people' | 'link' | null>(null);

  const people = useMemo(() => uniqueEmails(guests), [guests]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !saving) onCancel();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [saving, onCancel]);

  const title = files.length === 1
    ? (lang === 'vi'
      ? `Mọi người cần quyền truy cập vào "${files[0].title}"`
      : `People need access to "${files[0].title}"`)
    : (lang === 'vi'
      ? `Mọi người cần quyền truy cập vào ${files.length} tệp Drive`
      : `People need access to ${files.length} Drive files`);

  const roleSelectOptions = ROLE_OPTIONS.map((r) => ({
    value: r,
    label: t(roleLabelKey(r)),
  }));

  const radioClass =
    'mt-0.5 h-4 w-4 shrink-0 border-slate-400 text-brand-600 focus:ring-brand-500 accent-brand-600 dark:border-slate-500';

  const optionLabelClass =
    'block w-full text-left text-[14px] font-medium text-slate-900 dark:text-slate-100';

  const body = (
    <div
      className="fixed inset-0 z-[11000] flex items-center justify-center bg-slate-950/55 p-4 backdrop-blur-sm animate-[wh-fade_120ms_ease-out]"
      onClick={() => {
        if (!saving) onCancel();
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="calendar-drive-access-title"
        className="w-full max-w-[480px] overflow-hidden rounded-2xl bg-white shadow-2xl ring-1 ring-slate-900/10 dark:bg-slate-800 dark:ring-white/10 animate-[wh-pop_140ms_cubic-bezier(0.16,1,0.3,1)]"
        onClick={event => event.stopPropagation()}
        onMouseDown={event => event.stopPropagation()}
      >
        <div className="px-5 pb-2 pt-5">
          <div className="mb-5 flex items-start justify-between gap-3">
            <h2
              id="calendar-drive-access-title"
              className="max-w-[400px] text-[15px] font-semibold leading-snug text-slate-900 dark:text-slate-100"
            >
              {title}
            </h2>
            <HelpCircle className="mt-0.5 h-5 w-5 shrink-0 text-slate-400 dark:text-slate-500" />
          </div>

          <div className="space-y-5">
            {/* Option: chia sẻ với khách */}
            <div className="flex gap-3">
              <input
                type="radio"
                name="calendar-drive-access"
                className={radioClass}
                checked={choice === 'people'}
                onChange={() => setChoice('people')}
                disabled={saving}
                aria-label={lang === 'vi' ? 'Chia sẻ với khách' : 'Share with people'}
              />
              <div className="min-w-0 flex-1">
                <button
                  type="button"
                  className={optionLabelClass}
                  onClick={() => !saving && setChoice('people')}
                  disabled={saving}
                >
                  {lang === 'vi' ? 'Chia sẻ với khách' : 'Share with people'}
                </button>
                {choice === 'people' && (
                  <div className="mt-3 space-y-3">
                    <div className="flex flex-wrap gap-1.5">
                      {people.map(email => (
                        <span
                          key={email}
                          className="inline-flex max-w-full items-center gap-1.5 rounded-full bg-brand-100 px-2.5 py-1 text-[12px] text-brand-700 dark:bg-brand-500/15 dark:text-brand-300"
                        >
                          <UserCircle className="h-4 w-4 shrink-0" />
                          <span className="truncate">{email}</span>
                        </span>
                      ))}
                    </div>
                    <div className="w-[168px]" onMouseDown={e => e.stopPropagation()}>
                      <Select
                        value={role}
                        onChange={(v) => setRole(v as DrivePermissionRole)}
                        options={roleSelectOptions}
                        disabled={saving}
                        className="h-9 text-[13px]"
                        open={roleSelectOpen === 'people'}
                        onOpenChange={(next) => setRoleSelectOpen(next ? 'people' : null)}
                      />
                    </div>
                  </div>
                )}
              </div>
            </div>

            {/* Option: anyone with link */}
            <div className="flex gap-3">
              <input
                type="radio"
                name="calendar-drive-access"
                className={radioClass}
                checked={choice === 'link'}
                onChange={() => setChoice('link')}
                disabled={saving}
                aria-label={lang === 'vi' ? 'Cho phép bất kỳ ai có đường link truy cập' : 'Allow anyone with the link to access'}
              />
              <div className="min-w-0 flex-1">
                <button
                  type="button"
                  className={optionLabelClass}
                  onClick={() => !saving && setChoice('link')}
                  disabled={saving}
                >
                  {lang === 'vi' ? 'Cho phép bất kỳ ai có đường link truy cập' : 'Allow anyone with the link to access'}
                </button>
                {choice === 'link' && (
                  <div className="mt-3 w-[168px]" onMouseDown={e => e.stopPropagation()}>
                    <Select
                      value={role}
                      onChange={(v) => setRole(v as DrivePermissionRole)}
                      options={roleSelectOptions}
                      disabled={saving}
                      className="h-9 text-[13px]"
                      open={roleSelectOpen === 'link'}
                      onOpenChange={(next) => setRoleSelectOpen(next ? 'link' : null)}
                    />
                  </div>
                )}
              </div>
            </div>

            {/* Option: không cấp quyền */}
            <div className="flex gap-3">
              <input
                type="radio"
                name="calendar-drive-access"
                className={radioClass}
                checked={choice === 'none'}
                onChange={() => setChoice('none')}
                disabled={saving}
                aria-label={lang === 'vi' ? 'Không cấp quyền truy cập' : "Don't give access"}
              />
              <button
                type="button"
                className={optionLabelClass}
                onClick={() => !saving && setChoice('none')}
                disabled={saving}
              >
                {lang === 'vi' ? 'Không cấp quyền truy cập' : "Don't give access"}
              </button>
            </div>
          </div>
        </div>

        <div className="flex justify-end gap-2 border-t border-slate-100 bg-slate-50 px-5 py-3.5 dark:border-slate-700/60 dark:bg-slate-800/60">
          <button
            type="button"
            onClick={onCancel}
            disabled={saving}
            className="h-9 rounded-lg px-4 text-[13px] font-semibold text-slate-600 transition-colors hover:bg-slate-200/70 disabled:opacity-50 dark:text-slate-300 dark:hover:bg-slate-700"
          >
            {t('common.cancel')}
          </button>
          <button
            type="button"
            onClick={() => onSave(choice, role)}
            disabled={saving}
            className="inline-flex h-9 items-center gap-1.5 rounded-lg bg-brand-600 px-4 text-[13px] font-semibold text-white shadow-sm transition-colors hover:bg-brand-700 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400 disabled:opacity-60"
          >
            {saving && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            {lang === 'vi' ? 'Lưu sự kiện' : 'Save event'}
          </button>
        </div>
      </div>

      <style>{`
        @keyframes wh-fade { from { opacity: 0; } to { opacity: 1; } }
        @keyframes wh-pop { from { opacity: 0; transform: scale(0.96) translateY(6px); } to { opacity: 1; transform: scale(1) translateY(0); } }
      `}</style>
    </div>
  );

  return createPortal(body, document.body);
}
