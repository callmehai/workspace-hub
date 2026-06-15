import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from 'react-router-dom';
import { Toaster } from 'react-hot-toast';
import { AuthProvider } from './context/AuthContext';
import { router } from './router';

<<<<<<< HEAD
const queryClient = new QueryClient();
=======
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 1000 * 60 * 5, // 5 phút — tránh refetch dồn dập khi đổi tab/route
      retry: 1,
    },
  },
});
>>>>>>> bf126b6ce92ddd724c2438e9d963105f3b70e1b9

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
      <Toaster position="top-right" />
    </QueryClientProvider>
  );
}
