export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  limit: number;
}

export type ItemType = 'Email' | 'Event' | 'File' | 'Note' | 'Ticket';
export type ItemStatus = 'Inbox' | 'Doing' | 'Done';

/** Tag rút gọn nhúng trong item (BE trả kèm mỗi item — SCRUM-71). */
export interface ItemTag {
  id: string;
  name: string;
  color: string;
}

export interface ItemResponse {
  id: string;
  type: ItemType;
  title: string;
  snippet: string;
  status: ItemStatus;
  occurredAt: string;
  dueAt: string | null;
  isImportant: boolean;
  externalId: string | null;
  metadataJson: string | null;
  folderIds: string[];
  tags: ItemTag[];
  connectionId?: string | null;
  threadId?: string | null;
  /** Số message trong thread (Email gộp thread). 1 = thư đơn. */
  threadCount?: number;
  /** Populated when item is hydrated from calendar-details (not on list API). */
  reminders?: EventReminderDto[];
}

export interface UpdateItemStatusRequest {
  status: ItemStatus;
}

export interface CreateNoteRequest {
  title: string;
  contentMarkdown: string;
  folderId?: string;
}

export interface FolderResponse {
  id: string;
  name: string;
  color: string | null;
  icon: string | null;
  sortOrder: number;
  isArchived: boolean;
  itemCount: number;
  isOwner: boolean;
  permission: string;
  ownerName: string;
}

export interface CreateFolderRequest {
  name: string;
  color: string;
  icon: string;
}

export interface UpdateFolderRequest {
  name: string;
  color: string;
  icon: string;
  sortOrder: number;
  isArchived: boolean;
}

export interface AddItemToFolderRequest {
  itemId: string;
}

export interface ItemFolderResponse {
  itemId: string;
  folderId: string;
  position: number;
  addedAt: string;
}

export interface PatchItemRequest {
  isUnread?: boolean;
  isStarred?: boolean;
  addLabels?: string[];
  removeLabels?: string[];
  isTrashed?: boolean;
  title?: string;
  start?: string; // ISO DateTime
  end?: string; // ISO DateTime
  location?: string;
  attendees?: string[];
  /** Calendar UI contract: true = event cả ngày. BE Calendar cần map sang EventDateTime.Date. */
  allDay?: boolean;
  name?: string;
  statusTransition?: string;
  driveItemIds?: string[];
  // ── Jira (Type=Ticket) — SCRUM-57. Content is editable (unlike Email).
  summary?: string;
  description?: string;
  assignee?: string;           // Jira accountId
  priority?: string;           // priority name
  labels?: string[];           // replaces all labels (no spaces allowed per Jira)
  comment?: string;            // adds a new comment (separate operation)
  issueType?: string;          // change issue type (Task/Bug/Story...) via PUT /issue
  reminders?: EventReminderDto[];
  recurrence?: string[];
  guestsCanModify?: boolean;
  guestsCanInviteOthers?: boolean;
  guestsCanSeeOtherGuests?: boolean;
}

export interface EventReminderDto {
  id?: string | null;
  reminderType: ReminderType;
  offsetValue: number;
  offsetUnit: 'Minutes' | 'Hours' | 'Days' | 'Weeks';
  timeOfDay?: string; // "HH:mm" e.g., "09:00"
}

export type ReminderType = 'GooglePopup' | 'GoogleEmail' | 'InApp';

export interface CalendarEventAttendeeDto {
  email: string;
  displayName?: string | null;
  responseStatus?: string | null;
  comment?: string | null;
  organizer?: boolean;
}

export interface CalendarDriveAttachmentDto {
  fileId: string;
  title?: string | null;
  mimeType?: string | null;
  fileUrl?: string | null;
}

/** GET /api/items/{id}/calendar-details */
export interface CalendarEventDetailResponse {
  id: string;
  title: string;
  description?: string | null;
  start?: string | null;
  end?: string | null;
  allDay: boolean;
  location?: string | null;
  meetUrl?: string | null;
  htmlLink?: string | null;
  organizerEmail?: string | null;
  organizerDisplayName?: string | null;
  attendees: CalendarEventAttendeeDto[];
  driveAttachments: CalendarDriveAttachmentDto[];
  owningCalendarName?: string | null;
  reminders: EventReminderDto[];
  recurrence: string[];
  iCalUid?: string | null;
  guestsCanModify: boolean;
  guestsCanInviteOthers: boolean;
  guestsCanSeeOtherGuests: boolean;
  canEdit: boolean;
  canInviteOthers: boolean;
  canSeeGuestList: boolean;
  isOrganizer: boolean;
}

/**
 * POST /api/items/ticket — create a new Jira issue (SCRUM-56).
 * connectionId must be a Jira connection. Labels must not contain spaces.
 */
export interface CreateTicketRequest {
  connectionId: string;
  projectKey: string;
  issueType: string;
  summary: string;
  description?: string;
  assignee?: string;   // Jira accountId
  priority?: string;   // priority name
  labels?: string[];   // no whitespace in any label
}

export interface CreateEventRequest {
  connectionId: string;
  title: string;
  start: string;
  end: string;                // required; all-day: gửi ngày kế tiếp
  location?: string;
  attendees?: string[];
  description?: string;
  allDay?: boolean;
  driveItemIds?: string[];    // Item IDs (Guid) of Drive files to attach
  reminders?: EventReminderDto[];
  recurrence?: string[];
  guestsCanModify?: boolean;
  guestsCanInviteOthers?: boolean;
  guestsCanSeeOtherGuests?: boolean;
}

// ── Tags (SCRUM-70/71) ──
export interface TagResponse {
  id: string;
  name: string;
  color: string;
  itemCount: number;
}

export interface CreateTagRequest {
  name: string;
  color: string;
}

export interface UpdateTagRequest {
  name: string;
  color: string;
}
