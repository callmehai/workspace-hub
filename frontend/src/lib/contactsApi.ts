import api from './api';
import { odataEscape, parseODataResponse, type ODataResponse } from './odata';

export type GoogleContactSource = 'Contact' | 'OtherContact';

export interface ContactDto {
  id: string;
  connectionId: string;
  email: string;
  displayName?: string | null;
  source: GoogleContactSource;
  etag?: string | null;
  syncedAt: string;
  updatedAt?: string | null;
}

/** Gợi ý autocomplete — subset của ContactDto. */
export type ContactSuggestion = Pick<ContactDto, 'email' | 'displayName' | 'source'>;

export interface CreateContactRequest {
  connectionId: string;
  email: string;
  displayName?: string;
}

export interface PatchContactRequest {
  displayName?: string;
  email?: string;
  etag: string;
}

export interface GetContactsParams {
  source?: GoogleContactSource;
  search?: string;
  skip?: number;
  top?: number;
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

  /** Gợi ý To/Cc/Bcc — cùng endpoint OData, $select subset. */
  suggestContacts: async (connectionId: string, q: string, limit = 10): Promise<ContactSuggestion[]> => {
    if (q.trim().length < 2) return [];
    try {
      const term = odataEscape(q.trim());
      const filter = `contains(Email,'${term}') or contains(DisplayName,'${term}')`;
      const url =
        `/Contacts?connectionId=${connectionId}` +
        `&$filter=${encodeURIComponent(filter)}` +
        `&$top=${limit}` +
        `&$orderby=${encodeURIComponent('DisplayName')}` +
        `&$select=${encodeURIComponent('email,displayName,source')}`;
      const response = await api.get(url);
      const { value } = parseODataResponse<ContactSuggestion>(response.data);
      return value;
    } catch {
      return [];
    }
  },

  createContact: async (payload: CreateContactRequest): Promise<ContactDto> => {
    const response = await api.post<ContactDto>('/contacts', payload);
    return response.data;
  },

  updateContact: async (id: string, payload: PatchContactRequest): Promise<ContactDto> => {
    const response = await api.patch<ContactDto>(`/contacts/${id}`, payload);
    return response.data;
  },

  deleteContact: async (id: string): Promise<void> => {
    await api.delete(`/contacts/${id}`);
  },
};
