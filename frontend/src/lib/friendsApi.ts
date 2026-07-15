import api from './api';

export type FriendStatus = 'Pending' | 'Accepted';
export type FriendTier = 'Friend' | 'CloseFriend';

/** 1 quan hệ bạn bè dưới góc nhìn user hiện tại (counterpart = phía bên kia). */
export interface FriendDto {
  friendshipId: string;
  userId: string;
  email: string;
  fullName: string;
  avatarUrl: string | null;
  status: FriendStatus;
  myTier: FriendTier;
  isIncoming: boolean;
  createdAt: string;
  respondedAt: string | null;
}

/** Lời mời qua email (người nhận chưa có tài khoản). */
export interface FriendInviteDto {
  id: string;
  email: string;
  inviteLink: string;
  createdAt: string;
  expiresAt: string;
}

export interface FriendsOverview {
  friends: FriendDto[];
  incomingRequests: FriendDto[];
  outgoingRequests: FriendDto[];
  emailInvites: FriendInviteDto[];
}

export interface SendFriendRequestResult {
  outcome: 'RequestSent' | 'AutoAccepted' | 'InviteCreated';
  friend: FriendDto | null;
  invite: FriendInviteDto | null;
  emailSent: boolean;
}

export interface FriendInvitePublic {
  inviterName: string;
  email: string;
}

export const friendsApi = {
  getOverview: async (): Promise<FriendsOverview> => {
    const res = await api.get('/friends');
    return res.data;
  },

  sendRequest: async (email: string): Promise<SendFriendRequestResult> => {
    const res = await api.post('/friends/requests', { email });
    return res.data;
  },

  accept: async (friendshipId: string): Promise<FriendDto> => {
    const res = await api.post(`/friends/${friendshipId}/accept`);
    return res.data;
  },

  /** Pending = từ chối/hủy lời mời; Accepted = unfriend. */
  remove: async (friendshipId: string): Promise<void> => {
    await api.delete(`/friends/${friendshipId}`);
  },

  setTier: async (friendshipId: string, tier: FriendTier): Promise<FriendDto> => {
    const res = await api.patch(`/friends/${friendshipId}/tier`, { tier });
    return res.data;
  },

  cancelInvite: async (inviteId: string): Promise<void> => {
    await api.delete(`/friends/invites/${inviteId}`);
  },

  /** Public — banner trang đăng ký. 404 nếu token sai/hết hạn/đã dùng. */
  getInviteByToken: async (token: string): Promise<FriendInvitePublic> => {
    const res = await api.get(`/friends/invites/by-token/${token}`);
    return res.data;
  },
};
