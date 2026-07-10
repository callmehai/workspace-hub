import axios from 'axios';
import toast from 'react-hot-toast';
import type { TranslationKey } from '../i18n/translations';
import { handleApiError, type ApiErrorResponse } from './errorUtils';

/** BE ConflictException — duplicate email (không refresh list / đóng modal). */
export const CONTACT_DUPLICATE_EMAIL_BE =
  'A contact with this email already exists for this connection.';

/**
 * Message BE tĩnh (contacts) → key i18n. Phải khớp y hệt `message` hoặc phần sau `PropertyName: ` trong `details[]`.
 * Đồng bộ với: GoogleContactService, WriteBackGuard, PeopleGateway, Create/PatchContactRequestValidator.
 */
export const CONTACT_BE_ERROR_MAP: Record<string, TranslationKey> = {
  [CONTACT_DUPLICATE_EMAIL_BE]: 'errors.contact.duplicateEmail',
  'Server data has changed. Refreshing with the latest version.': 'errors.conflict',
  'Only saved contacts can be edited or deleted.': 'errors.contact.otherContactReadOnly',
  'Contact is not linked to Google. Please sync and try again.': 'errors.contact.noResourceName',
  'Only Gmail connections can be used for contacts.': 'errors.contact.gmailOnly',
  'Reconnect Gmail to allow editing contacts.': 'errors.contact.reconnectGmail',
  'ConnectionId is required.': 'errors.contact.connectionRequired',
  'Email is required.': 'errors.contact.emailRequired',
  'Email must be a valid address.': 'errors.contact.invalidEmail',
  'Email must be at most 320 characters.': 'errors.contact.emailTooLong',
  'DisplayName must be at most 256 characters.': 'errors.contact.displayNameTooLong',
  'Etag is required for PATCH.': 'errors.contact.etagRequired',
  'At least one of Email or DisplayName must be provided.': 'errors.contact.patchNeedsField',
};

/** Message BE động (id/status trong chuỗi) → key i18n. */
const CONTACT_BE_ERROR_PATTERNS: { test: RegExp; key: TranslationKey }[] = [
  { test: /^GoogleContact with id '.+' was not found\.$/, key: 'errors.contact.notFound' },
  { test: /^Connection with id '.+' was not found\.$/, key: 'errors.contact.connectionNotFound' },
  { test: /^Connection is not active \(status: .+\)\.$/, key: 'errors.contact.connectionInactive' },
];

function lookupContactBeKey(text: string): TranslationKey | undefined {
  const exact = CONTACT_BE_ERROR_MAP[text];
  if (exact) return exact;

  const colonIdx = text.indexOf(': ');
  if (colonIdx > 0) {
    const afterProperty = text.slice(colonIdx + 2);
    const fromDetail = CONTACT_BE_ERROR_MAP[afterProperty];
    if (fromDetail) return fromDetail;
  }

  return CONTACT_BE_ERROR_PATTERNS.find(({ test }) => test.test(text))?.key;
}

export function mapContactBeText(
  text: string,
  t: (key: TranslationKey) => string,
): string | undefined {
  const key = lookupContactBeKey(text);
  return key ? t(key) : undefined;
}

export function resolveContactApiError(
  err: unknown,
  t: (key: TranslationKey) => string,
): string | undefined {
  if (!axios.isAxiosError(err)) return undefined;

  const data = err.response?.data as ApiErrorResponse | undefined;
  if (!data) return undefined;

  if (data.message) {
    const mapped = mapContactBeText(data.message, t);
    if (mapped) return mapped;
  }

  if (data.details?.length) {
    for (const detail of data.details) {
      const mapped = mapContactBeText(detail, t);
      if (mapped) return mapped;
    }
  }

  return undefined;
}

function isContactEtagConflict(err: unknown): boolean {
  if (!axios.isAxiosError(err) || err.response?.status !== 409) return false;
  const msg = (err.response.data as ApiErrorResponse | undefined)?.message;
  return msg !== CONTACT_DUPLICATE_EMAIL_BE;
}

export function handleContactMutateError(
  err: unknown,
  t: (key: TranslationKey) => string,
  fallback: string,
  opts: { onConflict?: () => void; onForbidden?: () => void },
): void {
  const mapped = resolveContactApiError(err, t);

  if (mapped) {
    toast.error(mapped);
    if (axios.isAxiosError(err)) {
      const status = err.response?.status;
      if (status === 403) {
        opts.onForbidden?.();
      }
      if (status === 409 && isContactEtagConflict(err)) {
        opts.onConflict?.();
      }
    }
    return;
  }

  handleApiError(err, fallback, {
    onConflict: isContactEtagConflict(err) ? opts.onConflict : undefined,
  });
}