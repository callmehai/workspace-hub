import { Navigate, Outlet } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import { tokenStore } from '../../lib/api';

export const ProtectedRoute = () => {
  const { isAuthenticated, isLoading } = useAuth();

  const hasToken = tokenStore.get() !== null;

  if (isLoading && !hasToken) {
    return (
      <div className="flex h-screen w-screen items-center justify-center bg-gray-50">
        <div className="text-gray-500">Đang tải...</div>
      </div>
    );
  }

  if (!isAuthenticated && !hasToken) {
    // Chuyển hướng người dùng về trang đăng nhập nếu chưa đăng nhập
    return <Navigate to="/login" replace />;
  }

  // Hiển thị các component con nếu đã đăng nhập (sử dụng Outlet cho nested routes)
  return <Outlet />;
};
