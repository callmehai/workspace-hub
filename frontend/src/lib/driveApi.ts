import axios from 'axios';
import api from './api';
import type { TranslationKey } from '../i18n/translations';
import type { ItemResponse } from '../types/items';
import type {
    AddDrivePermissionPayload,
    CreateDriveFolderPayload,
    DriveFolderUploadEntry,
    DriveFolderUploadResponse,
    DrivePermission,
    DrivePermissionsListResponse,
    LinkSharingPayload,
    UpdateDrivePermissionPayload,
    UploadDriveFilePayload,
    UploadDriveFolderPayload,
} from '../types/drive';
import {
    MAX_DRIVE_FILE_BYTES,
    MAX_DRIVE_FOLDER_FILES,
    MAX_DRIVE_FOLDER_TOTAL_BYTES,
} from '../types/drive';

const CSRF_COOKIE = 'wh_csrf';
const CSRF_HEADER = 'X-CSRF-Token';

/** Timeout upload — file lớn cần lâu hơn api mặc định (30s). */
const UPLOAD_TIMEOUT_MS = 15 * 60 * 1000;

const readCookie = (name: string): string | null => {
    const match = document.cookie.match(new RegExp(`(?:^|; )${name}=([^;]*)`));
    return match ? decodeURIComponent(match[1]) : null;
};

/**
 * Axios riêng cho upload multipart — timeout dài, vẫn gửi cookie auth + CSRF.
 * Tách khỏi api.ts vì upload Drive có thể mất nhiều phút.
 */
const driveUploadClient = axios.create({
    baseURL: import.meta.env.VITE_API_URL ?? '/api',
    timeout: UPLOAD_TIMEOUT_MS,
    withCredentials: true,
});

driveUploadClient.interceptors.request.use((config) => {
    const csrf = readCookie(CSRF_COOKIE);
    if (csrf) {
        config.headers[CSRF_HEADER] = csrf;
    }
    return config;
});

/**
 * Validate danh sách file trước khi gọi API.
 * Trả key i18n (drive.upload.*) hoặc null nếu hợp lệ — UI dùng ở bước 6.
 */
export function validateDriveUploadFiles(files: File[]): TranslationKey | null {
    if (files.length === 0) return 'drive.upload.noFiles';

    if (files.length > MAX_DRIVE_FOLDER_FILES) return 'drive.upload.tooManyFiles';

    let total = 0;
    for (const f of files) {
        if (f.size <= 0) return 'drive.upload.emptyFile';
        if (f.size > MAX_DRIVE_FILE_BYTES) return 'drive.upload.fileTooBig';
        total += f.size;
    }

    if (total > MAX_DRIVE_FOLDER_TOTAL_BYTES) return 'drive.upload.folderTooBig';

    return null;
}

/** Build entries từ FileList (webkitdirectory) — lấy webkitRelativePath làm path gửi BE. */
export function buildDriveFolderEntries(files: FileList | File[]): DriveFolderUploadEntry[] {
    return Array.from(files).map((file) => ({
        file,
        relativePath: (file as File & { webkitRelativePath?: string }).webkitRelativePath || file.name,
    }));
}

export const driveApi = {
    /** Tạo folder trên Google Drive → trả Item mới (201). */
    createFolder: async (payload: CreateDriveFolderPayload): Promise<ItemResponse> => {
        const response = await api.post<ItemResponse>('/drive/folders', payload);
        return response.data;
    },

    /**
     * Upload một file từ máy lên Google Drive (multipart → BE → Google).
     * Gọi validateDriveUploadFiles([file]) trước nếu cần chặn sớm trên FE.
     */
    uploadFile: async (payload: UploadDriveFilePayload): Promise<ItemResponse> => {
        const form = new FormData();
        form.append('connectionId', payload.connectionId);
        if (payload.parentItemId) {
            form.append('parentItemId', payload.parentItemId);
        }
        form.append('file', payload.file);

        const response = await driveUploadClient.post<ItemResponse>('/drive/files', form);
        return response.data;
    },

    /**
     * Upload cả folder từ máy (webkitdirectory).
     * Gửi files[] + paths[] — BE tạo cây folder rồi upload từng file.
     */
    uploadFolder: async (payload: UploadDriveFolderPayload): Promise<DriveFolderUploadResponse> => {
        const form = new FormData();
        form.append('connectionId', payload.connectionId);
        if (payload.parentItemId) {
            form.append('parentItemId', payload.parentItemId);
        }
        for (const entry of payload.entries) {
            form.append('files', entry.file);
            form.append('paths', entry.relativePath);
        }

        const response = await driveUploadClient.post<DriveFolderUploadResponse>(
            '/drive/folders/upload',
            form,
        );
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
};

// Re-export hằng số để UI import từ driveApi nếu tiện
export {
    MAX_DRIVE_FILE_BYTES,
    MAX_DRIVE_FOLDER_FILES,
    MAX_DRIVE_FOLDER_TOTAL_BYTES,
} from '../types/drive';