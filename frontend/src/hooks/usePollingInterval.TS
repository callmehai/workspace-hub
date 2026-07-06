import { useEffect, useState } from 'react';

const DEFAULT_MS = 45_000;

/** Trả interval ms khi tab visible; false khi tab hidden → tắt TanStack Query poll. */
export function usePollingInterval(intervalMs = DEFAULT_MS): number | false {
    const [visible, setVisible] = useState(() => document.visibilityState === 'visible');

    useEffect(() => {
        const onChange = () => setVisible(document.visibilityState === 'visible');
        document.addEventListener('visibilitychange', onChange);
        return () => document.removeEventListener('visibilitychange', onChange);
    }, []);

    return visible ? intervalMs : false;
}