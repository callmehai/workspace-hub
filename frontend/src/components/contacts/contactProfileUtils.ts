import type { ContactDetailDto, ContactProfile, LabeledEmail, LabeledPhone } from '../../lib/contactsApi';

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

export function profileFromContact(contact: ContactDetailDto): ContactProfile {
  return {
    ...contact.profile,
    emails: contact.profile.emails.length > 0
      ? contact.profile.emails.map(e => ({ ...e }))
      : contact.email ? [{ value: contact.email }] : [{ value: '' }],
    phones: contact.profile.phones.map(p => ({ ...p })),
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
