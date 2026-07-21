/**
 * z-index menu/popover portal dùng chung (useFloatingMenu).
 * Phải cao hơn modal stack trong app — tham chiếu khi thêm modal mới:
 *   Calendar / SendEmail … 9000 · Confirm / DrivePicker … 10000 · DriveAccess … 11000
 */
export const PORTAL_MENU_Z_INDEX = 11100;
