const DEFAULT_MS = 45_000;

/**
 * Interval ms mặc định cho auto-refresh toàn app — xem `useBackgroundDataRefresh` (MainLayout).
 * Header notifications vẫn dùng hook này riêng.
 */
export function usePollingInterval(intervalMs = DEFAULT_MS): number {
  return intervalMs;
}
