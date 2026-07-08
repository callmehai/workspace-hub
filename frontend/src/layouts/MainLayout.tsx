import { useState, useEffect, useCallback } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { Sidebar } from '../components/layout/Sidebar';
import { Header } from '../components/layout/Header';

export const MainLayout = () => {
  const [mobileSidebarOpen, setMobileSidebarOpen] = useState(false);
  const location = useLocation();

  //Hàm callback tạo ra để tránh re-render khi location thay đổi, vì useEffect phụ thuộc vào hàm này
  const closeMobileSidebar = useCallback(() => setMobileSidebarOpen(false), []);

  //Update mobileSidebarOpen state when location.pathnam and location.search changes
  useEffect(() => {
    closeMobileSidebar();
  }, [location.pathname, location.search, closeMobileSidebar]);

  //Khi Sidebar đang mở thì lắng nghe phím Escape. Nếu người dùng nhấn Esc thì đóng Sidebar. Khi Sidebar đóng thì hủy việc lắng nghe.
  useEffect(() => {
    if (!mobileSidebarOpen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') closeMobileSidebar();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [mobileSidebarOpen, closeMobileSidebar]);

  //Sidebar đang mở thì disable scroll của body. Khi Sidebar đóng thì enable lại scroll của body.
  useEffect(() => {
    //nếu Sidebar đang đóng thì không cần disable scroll của body
    if (!mobileSidebarOpen) return;
    //còn mở Sidebar thì disable scroll của body. Lưu lại giá trị overflow trước đó để restore lại khi Sidebar đóng.
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    // Được gọi khi component unmount hoặc khi mobileSidebarOpen thay đổi. Restore lại giá trị overflow trước đó.
    return () => { document.body.style.overflow = prev; };
  }, [mobileSidebarOpen]);

  return (
    <div className="flex h-screen bg-slate-50 text-slate-800 dark:bg-slate-950 dark:text-slate-200 font-sans overflow-hidden">
      {mobileSidebarOpen && (
        <button
          type="button"
          aria-label="Close menu overlay"
          className="fixed inset-0 z-40 bg-black/40 lg:hidden"
          onClick={closeMobileSidebar}
        />
      )}

      <Sidebar mobileOpen={mobileSidebarOpen} onMobileClose={closeMobileSidebar} />

      <div className="flex-1 flex flex-col min-w-0">
        <Header onMenuClick={() => setMobileSidebarOpen(true)} />
        <main className="flex-1 overflow-y-auto overflow-x-hidden">
          <Outlet />
        </main>
      </div>
    </div>
  );
};