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
 * Dialog hỏi quyền Drive khi lưu event có đính kèm — bố cục gần DriveLinkRestrictDialog
 * (title 22px, body 14px, nút pill xanh Google) + Select role như DriveShareDialog.
 */
export function CalendarDriveAccessDialog({
  open,
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
  const missingCount = useMemo(
    () => files.reduce((count, file) => count + file.missingEmails.length, 0),
    [files],
  );

  // Mở lại dialog → reset lựa chọn mặc định.
  useEffect(() => {
    if (!open) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reset form khi mở dialog
    setChoice('people');
    setRole('reader');
    setRoleSelectOpen(null);
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !saving) onCancel();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, saving, onCancel]);

  if (!open) return null;

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
    'mt-0.5 h-4 w-4 shrink-0 border-slate-400 text-[#1a73e8] focus:ring-[#1a73e8]/accent-[#1a73e8]';

  const body = (
    <div
      className="fixed inset-0 z-[11000] flex items-center justify-center bg-black/40 p-4"
      onClick={() => {
        if (!saving) onCancel();
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="calendar-drive-access-title"
        className="w-full max-w-[480px] overflow-hidden rounded-3xl bg-white shadow-2xl dark:bg-slate-900"
        onClick={event => event.stopPropagation()}
        onMouseDown={event => event.stopPropagation()}
      >
        <div className="px-6 pb-2 pt-6">
          <div className="mb-5 flex items-start justify-between gap-3">
            <h2
              id="calendar-drive-access-title"
              className="max-w-[400px] text-[22px] font-normal leading-snug text-[#202124] dark:text-slate-100"
            >
              {title}
            </h2>
            <HelpCircle className="mt-1 h-5 w-5 shrink-0 text-[#5f6368] dark:text-slate-400" />
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
                  className="block w-full text-left text-[14px] font-medium text-[#202124] dark:text-slate-100"
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
                          className="inline-flex max-w-full items-center gap-1.5 rounded-full bg-[#e8f0fe] px-2.5 py-1 text-[12px] text-[#174ea6] dark:bg-blue-500/15 dark:text-blue-200"
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
                  className="block w-full text-left text-[14px] font-medium text-[#202124] dark:text-slate-100"
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
                className="block w-full text-left text-[14px] font-medium text-[#202124] dark:text-slate-100"
                onClick={() => !saving && setChoice('none')}
                disabled={saving}
              >
                {lang === 'vi' ? 'Không cấp quyền truy cập' : "Don't give access"}
              </button>
            </div>
          </div>

          {missingCount > 0 && (
            <p className="mt-5 text-[12px] leading-relaxed text-[#5f6368] dark:text-slate-400">
              {lang === 'vi'
                ? `${missingCount} lượt khách hiện chưa có quyền rõ ràng trên các tệp đính kèm.`
                : `${missingCount} guest access entries are missing across the attached files.`}
            </p>
          )}
        </div>

        <div className="flex justify-end gap-2 px-4 py-4">
          <button
            type="button"
            onClick={onCancel}
            disabled={saving}
            className="h-10 rounded-full px-4 text-[14px] font-medium text-[#1a73e8] hover:bg-[#f6fafe] disabled:opacity-50 dark:hover:bg-slate-800"
          >
            {t('common.cancel')}
          </button>
          <button
            type="button"
            onClick={() => onSave(choice, role)}
            disabled={saving}
            className="inline-flex h-10 items-center gap-2 rounded-full bg-[#1a73e8] px-6 text-[14px] font-medium text-white hover:bg-[#1765cc] disabled:opacity-50"
          >
            {saving && <Loader2 className="h-4 w-4 animate-spin" />}
            {lang === 'vi' ? 'Lưu sự kiện' : 'Save event'}
          </button>
        </div>
      </div>
    </div>
  );

  return createPortal(body, document.body);
}
