import { X } from 'lucide-react';

interface TagChipProps {
  name: string;
  color: string;
  /** Nếu có → hiện nút × để gỡ tag (dùng trong ItemDetail). */
  onRemove?: () => void;
  /** Cỡ nhỏ hơn cho list/board rows. */
  size?: 'sm' | 'md';
}

/**
 * Chip hiển thị 1 tag — nền tint theo màu tag + chữ màu tag (đồng nhất với chip Folder).
 * Có onRemove → thêm nút × để gỡ tag khỏi item.
 */
export function TagChip({ name, color, onRemove, size = 'md' }: TagChipProps) {
  const pad = size === 'sm' ? 'px-1.5 py-0 text-[10px]' : 'px-2 py-0.5 text-[11px]';
  return (
    <span
      className={`inline-flex items-center gap-1 rounded font-medium whitespace-nowrap ${pad}`}
      style={{ backgroundColor: `${color}20`, color }}
    >
      <span className="w-1.5 h-1.5 rounded-full shrink-0" style={{ backgroundColor: color }} />
      <span className="truncate max-w-[120px]">{name}</span>
      {onRemove && (
        <button
          type="button"
          onClick={(e) => { e.stopPropagation(); onRemove(); }}
          className="ml-0.5 -mr-0.5 rounded hover:bg-black/10 dark:hover:bg-white/10 transition-colors"
          aria-label="remove tag"
        >
          <X className="w-3 h-3" />
        </button>
      )}
    </span>
  );
}
