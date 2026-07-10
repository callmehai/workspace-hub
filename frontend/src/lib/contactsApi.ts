import api from './api';
import { odataEscape, parseODataResponse, type ODataResponse } from './odata';
import type { QueryClient } from '@tanstack/react-query';

export type GoogleContactSource = 'Contact' | 'OtherContact';

export interface ContactDto {
  id: string;
  connectionId: string;
  email?: string | null;
  displayName?: string | null;
  source: GoogleContactSource;
  etag?: string | null;
  syncedAt: string;
  updatedAt?: string | null;
}

export interface LabeledEmail {
  value: string;
  label?: string | null;
}

export interface LabeledPhone {
  value: string;
  label?: string | null;
}

export interface ContactBirthday {
  month?: number | null;
  day?: number | null;
  year?: number | null;
}

export interface ContactOrganization {
  name?: string | null;
  title?: string | null;
}

export interface ContactProfile {
  givenName?: string | null;
  familyName?: string | null;
  emails: LabeledEmail[];
  phones: LabeledPhone[];
  birthday?: ContactBirthday | null;
  organization?: ContactOrganization | null;
}

export interface ContactDetailDto extends ContactDto {
  profile: ContactProfile;
  readOnly: boolean;
}

/** Gợi ý autocomplete — luôn có email (flatten từ profile). */
export type ContactSuggestion = {
  email: string;
  displayName?: string | null;
  source: GoogleContactSource;
};

export interface CreateContactRequest {
  connectionId: string;
  email: string;
  displayName?: string;
  profile?: ContactProfile;
}

export interface PatchContactRequest {
  displayName?: string;
  email?: string;
  etag: string;
  profile?: ContactProfile;
}

export interface GetContactsParams {
  source?: GoogleContactSource;
  search?: string;
  skip?: number;
  top?: number;
}

/** Cùng logic hiển thị tên ở list + detail — ưu tiên Họ/Tên trong profile. */
export function contactDisplayName(
  contact: Pick<ContactDto, 'displayName' | 'email'> & { profile?: ContactProfile },
): string {
  const parts = [contact.profile?.givenName, contact.profile?.familyName]
    .filter((s): s is string => typeof s === 'string' && s.trim().length > 0);
  if (parts.length > 0) return parts.join(' ');
  return contact.displayName?.trim() || contact.email || '—';
}

/** Gộp row list khi detail/poll có data mới hơn (tránh list stale trong khi panel đã đúng). */
export function patchContactListCache(queryClient: QueryClient, contact: ContactDetailDto): void {
  const displayName = contactDisplayName(contact);
  queryClient.setQueriesData<ODataResponse<ContactDto>>(
    { queryKey: ['contacts'] },
    (cached) => {
      if (!cached?.value) return cached;
      const index = cached.value.findIndex((r) => r.id === contact.id);
      if (index < 0) return cached;
      const value = [...cached.value];
      value[index] = {
        ...value[index],
        displayName,
        email: contact.email,
        etag: contact.etag,
        updatedAt: contact.updatedAt,
        syncedAt: contact.syncedAt,
      };
      return { ...cached, value };
    },
  );
}

function buildContactsFilter(params: GetContactsParams): string | undefined {
  const parts: string[] = [];
  if (params.source) {
    parts.push(`Source eq '${params.source}'`);
  }
  if (params.search?.trim()) {
    const term = odataEscape(params.search.trim());
    parts.push(`(contains(Email,'${term}') or contains(DisplayName,'${term}'))`);
  }
  if (parts.length === 0) return undefined;
  return parts.join(' and ');
}

export const contactsApi = {
  getContacts: async (
    connectionId: string,
    params: GetContactsParams = {},
  ): Promise<ODataResponse<ContactDto>> => {
    const top = params.top ?? 20;
    const skip = params.skip ?? 0;
    let url =
      `/Contacts?connectionId=${connectionId}` +
      `&$top=${top}&$skip=${skip}&$count=true` +
      `&$orderby=${encodeURIComponent('DisplayName,Email')}`;
    const filter = buildContactsFilter(params);
    if (filter) url += `&$filter=${encodeURIComponent(filter)}`;
    const response = await api.get(url);
    return parseODataResponse<ContactDto>(response.data);
  },

  getContactById: async (id: string): Promise<ContactDetailDto> => {
    const response = await api.get<ContactDetailDto>(`/contacts/${id}`);
    return response.data;
  },

  /** Gợi ý To/Cc/Bcc — flatten mọi email trong profile. */
  suggestContacts: async (connectionId: string, query: string, limit = 10): Promise<ContactSuggestion[]> => {
    if (query.trim().length < 2) return [];
    try {
      const response = await api.get<ContactSuggestion[]>('/contacts/suggest', {
        params: { connectionId, query: query.trim(), limit },
      });
      return response.data;
    } catch {
      return [];
    }
  },

  createContact: async (payload: CreateContactRequest): Promise<ContactDto> => {
    const response = await api.post<ContactDto>('/contacts', payload);
    return response.data;
  },

  updateContact: async (id: string, payload: PatchContactRequest): Promise<ContactDetailDto> => {
    const response = await api.patch<ContactDetailDto>(`/contacts/${id}`, payload);
    return response.data;
  },

  deleteContact: async (id: string): Promise<void> => {
    await api.delete(`/contacts/${id}`);
  },
};
