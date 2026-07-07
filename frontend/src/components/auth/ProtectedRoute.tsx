import { Navigate, Outlet } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import { useI18n } from '../../hooks/useI18n';

export const ProtectedRoute = () => {
  const { isAuthenticated, isLoading } = useAuth();
  const { t } = useI18n();

  if (isLoading) {
    return (
      <div className="flex h-screen w-screen items-center justify-center bg-gray-50 dark:bg-slate-950">
        <div className="text-gray-500 dark:text-slate-300">{t('common.loading')}</div>
      </div>
    );
  }

  if (!isAuthenticated) {
    // Chuyển hướng người dùng về trang đăng nhập nếu chưa đăng nhập
    return <Navigate to="/login" replace />;
  }

  // Hiển thị các component con nếu đã đăng nhập (sử dụng Outlet cho nested routes)
  return <Outlet />;
};
