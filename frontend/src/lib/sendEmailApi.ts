import api from './api';

export interface SendEmailRequest {
  connectionId: string;
  to: string[];
  cc: string[];
  bcc: string[];
  subject: string;
  bodyHtml: string;
}

export interface SendEmailResult {
  messageId: string;
  sentAt: string;
}

export const sendEmailApi = {
  send: async (data: SendEmailRequest): Promise<SendEmailResult> => {
    const response = await api.post('/emails/send', data);
    return response.data;
  },

  getSignature: async (connectionId: string): Promise<string> => {
    const response = await api.get('/emails/signature', { params: { connectionId } });
    return response.data?.signature ?? '';
  },
};
