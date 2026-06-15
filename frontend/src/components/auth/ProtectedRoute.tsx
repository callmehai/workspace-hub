import { Navigate, Outlet } from 'react-router-dom';
<<<<<<< HEAD
import { useAuth } from '../../context/AuthContext';
=======
import { useAuth } from '../../hooks/useAuth';
>>>>>>> bf126b6ce92ddd724c2438e9d963105f3b70e1b9

export const ProtectedRoute = () => {
  const { isAuthenticated, isLoading } = useAuth();

  if (isLoading) {
    return (
      <div className="flex h-screen w-screen items-center justify-center bg-gray-50">
        <div className="text-gray-500">Đang tải...</div>
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
