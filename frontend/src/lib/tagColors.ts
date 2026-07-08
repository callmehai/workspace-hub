/** Bảng màu preset cho tag (chọn trong modal quản lý tag). Hex thuần, lưu thẳng vào Tag.Color. */
export const TAG_COLORS = [
  '#ef4444', // red
  '#f97316', // orange
  '#f59e0b', // amber
  '#eab308', // yellow
  '#22c55e', // green
  '#10b981', // emerald
  '#06b6d4', // cyan
  '#3b82f6', // blue
  '#6366f1', // indigo
  '#8b5cf6', // violet
  '#ec4899', // pink
  '#64748b', // slate
] as const;

export const DEFAULT_TAG_COLOR = TAG_COLORS[7]; // blue
