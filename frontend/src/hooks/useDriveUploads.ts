import { useEffect, useRef, useSyncExternalStore } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { subscribe, getSnapshot, type UploadTask } from '../lib/driveUploadStore';

/**
 * Đọc hàng đợi upload Drive (reactive) + tự invalidate ['items'] mỗi khi có file upload xong
 * (để list/board hiện file mới ngay). Dùng ở panel tiến độ (mount 1 lần ở MainLayout).
 */
export function useDriveUploads(): UploadTask[] {
  const tasks = useSyncExternalStore(subscribe, getSnapshot);
  const queryClient = useQueryClient();
  const prevFinishedRef = useRef(0);

  useEffect(() => {
    const finished = tasks.filter((t) => t.status === 'done' || t.status === 'error').length;
    if (finished !== prevFinishedRef.current) {
      prevFinishedRef.current = finished;
      if (finished > 0) queryClient.invalidateQueries({ queryKey: ['items'] });
    }
  }, [tasks, queryClient]);

  return tasks;
}
