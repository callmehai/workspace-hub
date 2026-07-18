import api from './api';
import type { ODataResponse } from './odata';
import type { AttachmentUpload } from './sendEmailApi';

export interface CreateScheduledEmailRequest {
  connectionId: string;
  to: string[];
  cc: string[];
  bcc: string[];
  subject: string;
  bodyHtml: string;
  attachments?: AttachmentUpload[];
  sendAt: string;
}

export interface ScheduledEmailDto {
  id: string;
  connectionId: string;
  to: string[];
  cc: string[];
  bcc: string[];
  subject: string;
  bodyHtml: string;
  sendAt: string;
  status: string;
  retryCount: number;
  lastError?: string;
  sentAt?: string;
  createdAt: string;
}

export const scheduledEmailsApi = {
  getScheduledEmails: async (skip: number = 0, top: number = 20, status?: string): Promise<ODataResponse<ScheduledEmailDto>> => {
    let url = `/ScheduledEmails?$top=${top}&$skip=${skip}&$count=true&$orderby=CreatedAt desc`;
    if (status && status !== 'All') {
      url += `&$filter=Status eq '${status}'`;
    }
    const response = await api.get(url);
    const data = response.data;
    // OData bọc trong { value: [...], '@odata.count': N }
    if (data && typeof data === 'object' && Array.isArray(data.value)) {
      return data as ODataResponse<ScheduledEmailDto>;
    }
    if (Array.isArray(data)) {
      return { value: data, '@odata.count': data.length };
    }
    return { value: [], '@odata.count': 0 };
  },

  getScheduledEmailById: async (id: string): Promise<ScheduledEmailDto> => {
    const response = await api.get(`/scheduled-emails/${id}`);
    return response.data;
  },

  createScheduledEmail: async (data: CreateScheduledEmailRequest): Promise<ScheduledEmailDto> => {
    const response = await api.post('/scheduled-emails', data);
    return response.data;
  },

  cancelScheduledEmail: async (id: string): Promise<void> => {
    await api.patch(`/scheduled-emails/${id}/cancel`);
  }
};
