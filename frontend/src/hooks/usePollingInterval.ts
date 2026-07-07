const DEFAULT_MS = 45_000;

/** Interval ms cho TanStack Query `refetchInterval` — poll đều kể cả tab browser nền. */
export function usePollingInterval(intervalMs = DEFAULT_MS): number {
  return intervalMs;
}
