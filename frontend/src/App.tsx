import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from 'react-router-dom';
import { Toaster } from 'react-hot-toast';
import { AuthProvider } from './context/AuthContext';
import { ThemeProvider } from './context/ThemeProvider';
import { I18nProvider } from './i18n/I18nProvider';
import { router } from './router';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 1000 * 60 * 5, // 5 phút — tránh refetch dồn dập khi đổi tab/route
      retry: 1,
    },
  },
});

export default function App() {
  // Thứ tự provider: Theme + I18n bọc NGOÀI Auth/Router để mọi nơi dùng được `useTheme`/`useI18n`.
  // Đổi theme/lang chỉ đổi context value → re-render, KHÔNG remount RouterProvider (không mất state/route).
  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        <I18nProvider>
          <AuthProvider>
            <RouterProvider router={router} />
          </AuthProvider>
          <Toaster
            position="top-right"
            containerClassName="!top-20"
            toastOptions={{
              className:
                '!bg-white dark:!bg-slate-800 !text-slate-800 dark:!text-slate-100 !border !border-slate-200 dark:!border-slate-700 !shadow-lg !rounded-lg',
            }}
          />
        </I18nProvider>
      </ThemeProvider>
    </QueryClientProvider>
  );
}
