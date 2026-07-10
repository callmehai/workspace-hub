import { User, Building2, Mail, Phone, Cake, Plus, Trash2 } from 'lucide-react';
import { useI18n } from '../../hooks/useI18n';
import type { ContactProfile, LabeledEmail, LabeledPhone } from '../../lib/contactsApi';

const EMAIL_LABELS = ['home', 'work', 'other'] as const;
const PHONE_LABELS = ['mobile', 'work', 'home', 'other'] as const;

export function emptyProfile(primaryEmail = ''): ContactProfile {
  return {
    givenName: '',
    familyName: '',
    emails: primaryEmail ? [{ value: primaryEmail }] : [{ value: '' }],
    phones: [],
    birthday: null,
    organization: null,
  };
}

export function normalizeProfile(profile: ContactProfile, fallbackEmail?: string): ContactProfile {
  const seen = new Set<string>();
  const rawEmails = profile.emails.length > 0
    ? profile.emails.map((e: LabeledEmail) => ({ ...e, value: e.value.trim().toLowerCase() }))
    : fallbackEmail ? [{ value: fallbackEmail.trim().toLowerCase() }] : [{ value: '' }];

  const emails: LabeledEmail[] = [];
  for (const entry of rawEmails) {
    if (!entry.value || seen.has(entry.value)) continue;
    seen.add(entry.value);
    emails.push({
      value: entry.value,
      label: entry.label?.trim().toLowerCase() || null,
    });
  }
  if (emails.length === 0) emails.push({ value: '' });

  return {
    givenName: profile.givenName?.trim() || null,
    familyName: profile.familyName?.trim() || null,
    emails,
    phones: profile.phones.map((p: LabeledPhone) => ({ ...p, value: p.value.trim() })).filter((p: LabeledPhone) => p.value),
    birthday: profile.birthday?.month && profile.birthday?.day
      ? {
          month: profile.birthday.month,
          day: profile.birthday.day,
          year: profile.birthday.year ?? null,
        }
      : null,
    organization: profile.organization?.name || profile.organization?.title
      ? {
          name: profile.organization?.name?.trim() || null,
          title: profile.organization?.title?.trim() || null,
        }
      : null,
  };
}

interface ContactProfileFormProps {
  profile: ContactProfile;
  onChange: (profile: ContactProfile) => void;
  disabled?: boolean;
}

export function ContactProfileForm({ profile, onChange, disabled }: ContactProfileFormProps) {
  const { t } = useI18n();

  const update = (patch: Partial<ContactProfile>) => onChange({ ...profile, ...patch });

  return (
    <div className="space-y-6">
      <section>
        <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-slate-400 mb-3">
          <User className="w-4 h-4" />
          {t('contacts.sectionName')}
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="block text-xs text-slate-500 mb-1">{t('contacts.givenName')}</label>
            <input
              type="text"
              disabled={disabled}
              value={profile.givenName ?? ''}
              onChange={e => update({ givenName: e.target.value })}
              className="w-full h-9 px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-sm"
            />
          </div>
          <div>
            <label className="block text-xs text-slate-500 mb-1">{t('contacts.familyName')}</label>
            <input
              type="text"
              disabled={disabled}
              value={profile.familyName ?? ''}
              onChange={e => update({ familyName: e.target.value })}
              className="w-full h-9 px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-sm"
            />
          </div>
        </div>
      </section>

      <section>
        <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-slate-400 mb-3">
          <Building2 className="w-4 h-4" />
          {t('contacts.sectionOrg')}
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="block text-xs text-slate-500 mb-1">{t('contacts.company')}</label>
            <input
              type="text"
              disabled={disabled}
              value={profile.organization?.name ?? ''}
              onChange={e => update({ organization: { ...profile.organization, name: e.target.value } })}
              className="w-full h-9 px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-sm"
            />
          </div>
          <div>
            <label className="block text-xs text-slate-500 mb-1">{t('contacts.jobTitle')}</label>
            <input
              type="text"
              disabled={disabled}
              value={profile.organization?.title ?? ''}
              onChange={e => update({ organization: { ...profile.organization, title: e.target.value } })}
              className="w-full h-9 px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-sm"
            />
          </div>
        </div>
      </section>

      <section>
        <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-slate-400 mb-3">
          <Mail className="w-4 h-4" />
          {t('contacts.email')}
        </div>
        <div className="space-y-2">
          {profile.emails.map((row: LabeledEmail, idx: number) => (
            <div key={idx} className="flex gap-2">
              <input
                type="email"
                disabled={disabled}
                value={row.value}
                onChange={e => {
                  const emails = [...profile.emails];
                  emails[idx] = { ...emails[idx], value: e.target.value };
                  update({ emails });
                }}
                className="flex-1 h-9 px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-sm"
              />
              <select
                disabled={disabled}
                value={row.label ?? ''}
                onChange={e => {
                  const emails = [...profile.emails];
                  emails[idx] = { ...emails[idx], label: e.target.value || null };
                  update({ emails });
                }}
                className="h-9 px-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-sm"
              >
                <option value="">{t('contacts.labelNone')}</option>
                {EMAIL_LABELS.map(l => (
                  <option key={l} value={l}>{t(`contacts.emailLabel.${l}`)}</option>
                ))}
              </select>
              {profile.emails.length > 1 && !disabled && (
                <button
                  type="button"
                  onClick={() => update({ emails: profile.emails.filter((_: LabeledEmail, i: number) => i !== idx) })}
                  className="p-2 text-slate-400 hover:text-rose-600"
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              )}
            </div>
          ))}
          {!disabled && (
            <button
              type="button"
              onClick={() => update({ emails: [...profile.emails, { value: '' }] })}
              className="inline-flex items-center gap-1 text-sm text-brand-600 hover:underline"
            >
              <Plus className="w-4 h-4" />
              {t('contacts.addEmail')}
            </button>
          )}
        </div>
      </section>

      <section>
        <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-slate-400 mb-3">
          <Phone className="w-4 h-4" />
          {t('contacts.phone')}
        </div>
        <div className="space-y-2">
          {profile.phones.length === 0 && disabled && (
            <p className="text-sm text-slate-400">—</p>
          )}
          {profile.phones.map((row: LabeledPhone, idx: number) => (
            <div key={idx} className="flex gap-2">
              <input
                type="tel"
                disabled={disabled}
                value={row.value}
                placeholder="+84..."
                onChange={e => {
                  const phones = [...profile.phones];
                  phones[idx] = { ...phones[idx], value: e.target.value };
                  update({ phones });
                }}
                className="flex-1 h-9 px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-sm"
              />
              <select
                disabled={disabled}
                value={row.label ?? ''}
                onChange={e => {
                  const phones = [...profile.phones];
                  phones[idx] = { ...phones[idx], label: e.target.value || null };
                  update({ phones });
                }}
                className="h-9 px-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-sm"
              >
                <option value="">{t('contacts.labelNone')}</option>
                {PHONE_LABELS.map(l => (
                  <option key={l} value={l}>{t(`contacts.phoneLabel.${l}`)}</option>
                ))}
              </select>
              {!disabled && (
                <button
                  type="button"
                  onClick={() => update({ phones: profile.phones.filter((_: LabeledPhone, i: number) => i !== idx) })}
                  className="p-2 text-slate-400 hover:text-rose-600"
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              )}
            </div>
          ))}
          {!disabled && (
            <button
              type="button"
              onClick={() => update({ phones: [...profile.phones, { value: '' }] })}
              className="inline-flex items-center gap-1 text-sm text-brand-600 hover:underline"
            >
              <Plus className="w-4 h-4" />
              {t('contacts.addPhone')}
            </button>
          )}
        </div>
      </section>

      <section>
        <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-slate-400 mb-3">
          <Cake className="w-4 h-4" />
          {t('contacts.birthday')}
        </div>
        <div className="grid grid-cols-3 gap-3">
          <div>
            <label className="block text-xs text-slate-500 mb-1">{t('contacts.birthDay')}</label>
            <input
              type="number"
              min={1}
              max={31}
              disabled={disabled}
              value={profile.birthday?.day ?? ''}
              onChange={e => update({
                birthday: {
                  ...profile.birthday,
                  day: e.target.value ? Number(e.target.value) : null,
                  month: profile.birthday?.month ?? null,
                  year: profile.birthday?.year ?? null,
                },
              })}
              className="w-full h-9 px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-sm"
            />
          </div>
          <div>
            <label className="block text-xs text-slate-500 mb-1">{t('contacts.birthMonth')}</label>
            <input
              type="number"
              min={1}
              max={12}
              disabled={disabled}
              value={profile.birthday?.month ?? ''}
              onChange={e => update({
                birthday: {
                  ...profile.birthday,
                  month: e.target.value ? Number(e.target.value) : null,
                  day: profile.birthday?.day ?? null,
                  year: profile.birthday?.year ?? null,
                },
              })}
              className="w-full h-9 px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-sm"
            />
          </div>
          <div>
            <label className="block text-xs text-slate-500 mb-1">{t('contacts.birthYear')}</label>
            <input
              type="number"
              min={1}
              disabled={disabled}
              value={profile.birthday?.year ?? ''}
              onChange={e => update({
                birthday: {
                  ...profile.birthday,
                  year: e.target.value ? Number(e.target.value) : null,
                  day: profile.birthday?.day ?? null,
                  month: profile.birthday?.month ?? null,
                },
              })}
              className="w-full h-9 px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-sm"
            />
          </div>
        </div>
      </section>
    </div>
  );
}
