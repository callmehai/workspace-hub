import { useState, useEffect, useCallback } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { Sidebar } from '../components/layout/Sidebar';
import { Header } from '../components/layout/Header';
import { useNotificationHub } from '../hooks/useNotificationHub';

export const MainLayout = () => {
  useNotificationHub();
  const [mobileSidebarOpen, setMobileSidebarOpen] = useState(false);
  const location = useLocation();

  //Hàm callback tạo ra để tránh re-render khi location thay đổi, vì useEffect phụ thuộc vào hàm này
  const closeMobileSidebar = useCallback(() => setMobileSidebarOpen(false), []);

  // Đóng drawer khi đổi route (mobile UX)
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reset drawer khi pathname/search đổi
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

  // Sidebar đang mở thì disable scroll của body. Khi Sidebar đóng thì enable lại scroll của body.
  useEffect(() => {
    if (!mobileSidebarOpen) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => { document.body.style.overflow = prev; };
  }, [mobileSidebarOpen]);

  // Đóng drawer khi resize/xoay ngang qua breakpoint desktop (lg) — tránh body scroll kẹt hidden
  useEffect(() => {
    const mq = window.matchMedia('(min-width: 1024px)');
    const onDesktop = (e: MediaQueryListEvent) => {
      if (e.matches) closeMobileSidebar();
    };
    mq.addEventListener('change', onDesktop);
    return () => mq.removeEventListener('change', onDesktop);
  }, [closeMobileSidebar]);

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