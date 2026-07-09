import api from "./api"
import type { ItemResponse } from "../types/items"
import type {
    AddDrivePermissionPayload,
    CreateDriveFolderPayload,
    DrivePermission,
    DrivePermissionsListResponse,
    LinkSharingPayload,
    UpdateDrivePermissionPayload,
} from '../types/drive';

export const driveApi = {
    /** Tạo folder trên Google Drive → trả Item mới (201). */
    createFolder: async (payload: CreateDriveFolderPayload): Promise<ItemResponse> => {
        const response = await api.post<ItemResponse>('/drive/folders', payload);
        return response.data;
    },

    /** Danh sách quyền share của file/folder Drive. */
    listPermissions: async (itemId: string): Promise<DrivePermissionsListResponse> => {
        const response = await api.get<DrivePermissionsListResponse>(`/drive/items/${itemId}/permissions`);
        return response.data;
    },

    /** Mời email chia sẻ. */
    addPermission: async (
        itemId: string,
        payload: AddDrivePermissionPayload,
    ): Promise<DrivePermission> => {
        const response = await api.post<DrivePermission>(
            `/drive/items/${itemId}/permissions`,
            payload,
        );
        return response.data;
    },

    /** Đổi role (không áp dụng owner). */
    updatePermission: async (
        itemId: string,
        permissionId: string,
        payload: UpdateDrivePermissionPayload,
    ): Promise<DrivePermission> => {
        const response = await api.patch<DrivePermission>(
            `/drive/items/${itemId}/permissions/${permissionId}`,
            payload,
        );
        return response.data;
    },

    /** Gỡ quyền share. */
    removePermission: async (itemId: string, permissionId: string): Promise<void> => {
        await api.delete(`/drive/items/${itemId}/permissions/${permissionId}`);
    },

    /** Bật/tắt link "ai có đường link". */
    setLinkSharing: async (
        itemId: string,
        payload: LinkSharingPayload,
    ): Promise<DrivePermission | null> => {
        const response = await api.put<DrivePermission | null>(
            `/drive/items/${itemId}/link-sharing`,
            payload,
        );
        return response.data;
    },


}