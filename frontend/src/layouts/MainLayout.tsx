import { useState, useEffect, useCallback } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { Sidebar } from '../components/layout/Sidebar';
import { Header } from '../components/layout/Header';
import { useNotificationHub } from '../hooks/useNotificationHub';

export const MainLayout = () => {
  useNotificationHub();
  const [mobileSidebarOpen, setMobileSidebarOpen] = useState(false);
  const location = useLocation();

  const closeMobileSidebar = useCallback(() => setMobileSidebarOpen(false), []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reset drawer khi pathname/search đổi
    closeMobileSidebar();
  }, [location.pathname, location.search, closeMobileSidebar]);

  useEffect(() => {
    if (!mobileSidebarOpen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') closeMobileSidebar();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [mobileSidebarOpen, closeMobileSidebar]);

  useEffect(() => {
    if (!mobileSidebarOpen) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = prev;
    };
  }, [mobileSidebarOpen]);

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
        {/* scrollbar-gutter:stable → luôn chừa chỗ cho thanh cuộn dọc, tránh nội dung
            (căn giữa mx-auto) bị dịch vài px khi đổi giữa view Danh sách (có cuộn) và Bảng (không cuộn). */}
        <main className="flex-1 overflow-y-auto overflow-x-hidden [scrollbar-gutter:stable]">
          <Outlet />
        </main>
      </div>
    </div>
  );
};
