import { Bell } from 'lucide-react';
import toast from 'react-hot-toast';

/** Toast item mới — dùng `toast()` + style global Toaster (giống toast.error/success). */
export function showNotificationToast(
  title: string,
  body: string | null | undefined,
  onNavigate: () => void,
) {
  toast(
    (t) => (
      <button
        type="button"
        className={`${t.visible ? 'animate-enter' : 'animate-leave'} w-full cursor-pointer text-left`}
        onClick={() => {
          onNavigate();
          toast.dismiss(t.id);
        }}
      >
        <p className="text-sm font-medium">{title}</p>
        {body ? (
          <p className="mt-0.5 text-xs text-slate-500 dark:text-slate-400 line-clamp-2">{body}</p>
        ) : null}
      </button>
    ),
    {
      duration: 8000,
      icon: <Bell className="h-5 w-5 shrink-0 text-brand-600 dark:text-brand-400" aria-hidden />,
    },
  );
}
