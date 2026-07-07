import { Navigate, Outlet } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';

export const AdminRoute = () => {
  const { isLoading, user } = useAuth();

  if (isLoading) {
    return (
      <div className="flex h-screen w-screen items-center justify-center bg-gray-50 dark:bg-slate-950">
        <div className="text-gray-500 dark:text-slate-300">Đang tải...</div>
      </div>
    );
  }

  if (user?.role !== 'Admin') {
    return <Navigate to="/" replace />;
  }

  return <Outlet />;
};
