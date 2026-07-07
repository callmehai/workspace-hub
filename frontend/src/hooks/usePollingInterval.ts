const DEFAULT_MS = 45_000;

/**
 * Interval ms cho TanStack Query `refetchInterval`.
 * Cần kèm `refetchIntervalInBackground: true` trên từng query — RQ v5 vẫn tạm dừng
 * interval poll khi tab hidden nếu thiếu flag đó.
 */
export function usePollingInterval(intervalMs = DEFAULT_MS): number {
  return intervalMs;
}
