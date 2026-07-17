import type { TranslationKey } from '../i18n/translations';

/** Ngưỡng tải ảnh gốc làm preview (ảnh lớn hơn dùng thumbnail để đỡ tốn RAM/băng thông). */
export const PREVIEW_IMAGE_MAX_BYTES = 25 * 1024 * 1024;

const GOOGLE_APPS_PREFIX = 'application/vnd.google-apps';

export function isFolderMime(mime: string | null | undefined): boolean {
  return mime === `${GOOGLE_APPS_PREFIX}.folder`;
}

export function isImageMime(mime: string | null | undefined): boolean {
  return !!mime && mime.startsWith('image/');
}

/** Loại có thumbnail hữu ích từ Google (ảnh/video/pdf/google-docs...) — folder thì không. */
export function supportsThumbnail(mime: string | null | undefined): boolean {
  if (!mime || isFolderMime(mime)) return false;
  return (
    mime.startsWith('image/') ||
    mime.startsWith('video/') ||
    mime === 'application/pdf' ||
    mime.startsWith(GOOGLE_APPS_PREFIX)
  );
}

export type DrivePreviewKind = 'image' | 'thumbnail' | 'icon';

/**
 * Cách preview 1 file trong drawer:
 *  - 'image'     → tải nội dung gốc (ảnh nhỏ) hiển thị <img>.
 *  - 'thumbnail' → xin thumbnail Google (ảnh lớn / video / pdf / google-docs).
 *  - 'icon'      → không preview được → icon loại lớn.
 */
export function drivePreviewKind(mime: string | null | undefined, sizeBytes: number | null | undefined): DrivePreviewKind {
  if (isImageMime(mime) && (sizeBytes == null || sizeBytes <= PREVIEW_IMAGE_MAX_BYTES)) {
    return 'image';
  }
  if (supportsThumbnail(mime)) return 'thumbnail';
  return 'icon';
}

/** Đuôi mở rộng in hoa từ subtype (image/png → "PNG"). */
function subtypeExt(mime: string): string {
  const sub = mime.split('/')[1] ?? '';
  const clean = sub.split('+')[0].split('.').pop() ?? sub;
  return clean.toUpperCase();
}

/**
 * Nhãn loại file thân thiện thay cho mimeType thô ("image/png" → "Ảnh PNG").
 * Trả về chuỗi đã dịch theo ngôn ngữ (nhận `t`).
 */
export function friendlyMimeLabel(mime: string | null | undefined, t: (k: TranslationKey) => string): string {
  if (!mime) return t('drive.mime.file');
  if (isFolderMime(mime)) return t('drive.mime.folder');

  switch (mime) {
    case 'application/pdf':
      return t('drive.mime.pdf');
    case `${GOOGLE_APPS_PREFIX}.document`:
      return t('drive.mime.gdoc');
    case `${GOOGLE_APPS_PREFIX}.spreadsheet`:
      return t('drive.mime.gsheet');
    case `${GOOGLE_APPS_PREFIX}.presentation`:
      return t('drive.mime.gslide');
    case `${GOOGLE_APPS_PREFIX}.form`:
      return t('drive.mime.gform');
    case 'application/zip':
    case 'application/x-rar-compressed':
    case 'application/x-7z-compressed':
    case 'application/x-tar':
    case 'application/gzip':
      return t('drive.mime.archive');
    case 'application/msword':
    case 'application/vnd.openxmlformats-officedocument.wordprocessingml.document':
      return 'Word';
    case 'application/vnd.ms-excel':
    case 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet':
      return t('drive.mime.sheet');
    case 'application/vnd.ms-powerpoint':
    case 'application/vnd.openxmlformats-officedocument.presentationml.presentation':
      return t('drive.mime.slide');
  }

  if (mime.startsWith('image/')) return `${t('drive.mime.image')} ${subtypeExt(mime)}`;
  if (mime.startsWith('video/')) return `${t('drive.mime.video')} ${subtypeExt(mime)}`;
  if (mime.startsWith('audio/')) return `${t('drive.mime.audio')} ${subtypeExt(mime)}`;
  if (mime.startsWith('text/')) return `${t('drive.mime.text')} ${subtypeExt(mime)}`;

  return t('drive.mime.file');
}
