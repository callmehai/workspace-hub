import { X, Tag as TagIcon, Folder as FolderIcon, type LucideIcon } from 'lucide-react';

interface ChipProps {
  name: string;
  color: string;
  /** Nếu có → hiện nút × để gỡ (tag khỏi item / item khỏi folder). */
  onRemove?: () => void;
  /** Cỡ nhỏ hơn cho list/board rows. */
  size?: 'sm' | 'md';
}

/**
 * Chip nền tint theo màu + viền + icon + chữ cùng màu. Dùng CHUNG cho Tag & Folder
 * (chỉ khác icon: tag = icon tag, folder = icon folder) để 2 loại chip nhất quán UI.
 */
function BaseChip({ name, color, icon: Icon, onRemove, size = 'md' }: ChipProps & { icon: LucideIcon }) {
  const pad = size === 'sm' ? 'px-1.5 py-0.5 text-[10px] gap-1' : 'px-2 py-0.5 text-[11px] gap-1';
  const ic = size === 'sm' ? 'w-2.5 h-2.5' : 'w-3 h-3';
  return (
    <span
      className={`inline-flex items-center rounded font-medium whitespace-nowrap ${pad}`}
      style={{ backgroundColor: `${color}20`, color, border: `1px solid ${color}40` }}
    >
      <Icon className={`${ic} shrink-0`} />
      <span className="truncate max-w-[120px]">{name}</span>
      {onRemove && (
        <button
          type="button"
          onClick={(e) => { e.stopPropagation(); onRemove(); }}
          className="ml-0.5 -mr-0.5 rounded hover:bg-black/10 dark:hover:bg-white/10 transition-colors"
          aria-label="remove"
        >
          <X className="w-3 h-3" />
        </button>
      )}
    </span>
  );
}

/** Chip tag — icon tag. */
export function TagChip(props: ChipProps) {
  return <BaseChip {...props} icon={TagIcon} />;
}

/** Chip folder — icon folder, cùng UI với TagChip. */
export function FolderChip(props: ChipProps) {
  return <BaseChip {...props} icon={FolderIcon} />;
}
