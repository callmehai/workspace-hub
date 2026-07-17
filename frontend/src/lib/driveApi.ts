import axios from 'axios';
import api from './api';
import type { TranslationKey } from '../i18n/translations';
import type { ItemResponse } from '../types/items';
import type {
    AddDrivePermissionPayload,
    CreateDriveFolderPayload,
    DriveFolderUploadEntry,
    DriveFolderUploadResponse,
    DriveLinkRestrictConflict,
    DrivePermission,
    DrivePermissionsListResponse,
    LinkSharingPayload,
    UpdateDrivePermissionPayload,
    UploadDriveFilePayload,
    UploadDriveFolderPayload,
} from '../types/drive';
import { LINK_RESTRICT_AFFECTS_PARENT } from '../types/drive';
import {
    MAX_DRIVE_FILE_BYTES,
    MAX_DRIVE_FOLDER_FILES,
    MAX_DRIVE_FOLDER_TOTAL_BYTES,
} from '../types/drive';

/**
 * Timeout upload — file lớn cần lâu hơn api mặc định (30s).
 * Dùng chung instance `api` (CSRF + refresh 401) — chỉ override timeout theo request.
 */
const UPLOAD_TIMEOUT_MS = 15 * 60 * 1000;

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
     * Dùng `api` chung → hết access token giữa upload vẫn refresh + retry như mọi API khác.
     */
    uploadFile: async (payload: UploadDriveFilePayload): Promise<ItemResponse> => {
        const form = new FormData();
        form.append('connectionId', payload.connectionId);
        if (payload.parentItemId) {
            form.append('parentItemId', payload.parentItemId);
        }
        form.append('file', payload.file);

        const response = await api.post<ItemResponse>('/drive/files', form, {
            timeout: UPLOAD_TIMEOUT_MS,
        });
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

        const response = await api.post<DriveFolderUploadResponse>(
            '/drive/folders/upload',
            form,
            { timeout: UPLOAD_TIMEOUT_MS },
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

    /** Bật/tắt link "ai có đường link". Case 1 chưa confirm → BE 409 + conflict body. */
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

    /**
     * Preview Case 1 — 200 conflict hoặc null (204).
     * FE có thể gọi trước khi tắt link; hoặc bắt 409 từ setLinkSharing.
     */
    getLinkRestrictConflict: async (itemId: string): Promise<DriveLinkRestrictConflict | null> => {
        const response = await api.get<DriveLinkRestrictConflict>(
            `/drive/items/${itemId}/link-sharing/restrict-conflict`,
            { validateStatus: (s) => s === 200 || s === 204 },
        );
        if (response.status === 204) return null;
        return response.data;
    },

    /** Blob nội dung file (preview ảnh) — qua axios để hưởng refresh 401 + cookie auth. */
    fetchContentBlob: async (itemId: string): Promise<Blob> => {
        const response = await api.get(`/drive/items/${itemId}/content`, {
            responseType: 'blob',
            timeout: UPLOAD_TIMEOUT_MS,
        });
        return response.data as Blob;
    },

    /** Thumbnail preview — Blob hoặc null (204 = file không có thumbnail). */
    fetchThumbnailBlob: async (itemId: string): Promise<Blob | null> => {
        const response = await api.get(`/drive/items/${itemId}/thumbnail`, {
            responseType: 'blob',
            validateStatus: (s) => s === 200 || s === 204,
        });
        if (response.status === 204) return null;
        const blob = response.data as Blob;
        return blob && blob.size > 0 ? blob : null;
    },

    /**
     * Tải file xuống. Probe /auth/me trước để interceptor refresh token nếu hết hạn,
     * rồi mở link <a download> — trình duyệt stream thẳng ra đĩa (không buffer 100MB vào RAM).
     */
    downloadFile: async (itemId: string, fileName?: string): Promise<void> => {
        try {
            await api.get('/auth/me');
        } catch {
            // Interceptor lo refresh/redirect; nếu thật sự hết phiên sẽ về /login.
        }
        const a = document.createElement('a');
        a.href = driveContentUrl(itemId, { download: true });
        if (fileName) a.download = fileName;
        a.rel = 'noopener';
        document.body.appendChild(a);
        a.click();
        a.remove();
    },
};

/** URL tương đối tới nội dung file — dùng cho thẻ <a download> / <img> fallback (cookie auth tự gửi). */
export function driveContentUrl(itemId: string, opts?: { download?: boolean }): string {
    const base = api.defaults.baseURL ?? '/api';
    const query = opts?.download ? '?dl=true' : '';
    return `${base}/drive/items/${itemId}/content${query}`;
}

/** Lấy DriveLinkRestrictConflict từ lỗi axios 409 (PUT tắt link Case 1). */
export function getLinkRestrictConflictFromError(err: unknown): DriveLinkRestrictConflict | null {
    if (!axios.isAxiosError(err) || err.response?.status !== 409) return null;
    const data = err.response.data as DriveLinkRestrictConflict | undefined;
    if (!data || data.code !== LINK_RESTRICT_AFFECTS_PARENT) return null;
    return data;
}

// Re-export hằng số để UI import từ driveApi nếu tiện
export {
    MAX_DRIVE_FILE_BYTES,
    MAX_DRIVE_FOLDER_FILES,
    MAX_DRIVE_FOLDER_TOTAL_BYTES,
} from '../types/drive';
