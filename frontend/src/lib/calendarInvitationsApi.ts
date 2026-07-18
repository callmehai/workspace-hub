import api from './api';

export type CalendarInvitationStatus = 'NeedsAction' | 'Accepted' | 'Tentative' | 'Declined';

export interface CalendarInvitation {
  id: string;
  organizerItemId: string;
  inviteeItemId?: string | null;
  inviteeEmail: string;
  organizerEmail: string;
  organizerName: string;
  status: CalendarInvitationStatus;
  googleSyncPending: boolean;
  title: string;
  description?: string | null;
  start: string;
  end: string;
  allDay: boolean;
  location?: string | null;
  attendees: string[];
  iCalUid?: string | null;
  guestsCanModify: boolean;
  guestsCanInviteOthers: boolean;
  guestsCanSeeOtherGuests: boolean;
}

export const calendarInvitationsApi = {
  list: async (from?: string, to?: string): Promise<CalendarInvitation[]> => {
    const response = await api.get('/calendarinvitations', { params: { from, to } });
    return response.data;
  },
  get: async (id: string): Promise<CalendarInvitation> => {
    const response = await api.get(`/calendarinvitations/${id}`);
    return response.data;
  },
  respond: async (
    id: string,
    responseStatus: Exclude<CalendarInvitationStatus, 'NeedsAction'>,
    comment?: string,
  ): Promise<CalendarInvitation> => {
    const response = await api.post(`/calendarinvitations/${id}/respond`, {
      response: responseStatus,
      comment,
    });
    return response.data;
  },
};
