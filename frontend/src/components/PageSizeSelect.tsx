import { Select } from './Select';

interface Props {
  value: number;
  /** Đổi page size — trang gọi nhớ reset page về 1 */
  onChange: (size: number) => void;
}

/**
 * Chọn số item / trang (5-10-20-50-100) — dùng chung cho mọi trang có phân trang.
 * Dùng Select custom (KHÔNG native <select>) + dropUp vì footer nằm đáy trang.
 */
export const PageSizeSelect = ({ value, onChange }: Props) => (
  <label className="inline-flex items-center gap-1.5 text-[12.5px] text-slate-500 whitespace-nowrap">
    Hiển thị
    <Select
      value={String(value)}
      onChange={(v) => onChange(Number(v))}
      options={[5, 10, 20, 50, 100].map(n => ({ value: String(n), label: String(n) }))}
      className="h-8 w-[68px] text-[12.5px]"
      dropUp
    />
    / trang
  </label>
);
