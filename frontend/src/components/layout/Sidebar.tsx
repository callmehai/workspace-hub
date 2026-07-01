import { NavLink, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import {
  Inbox,
  Kanban,
  Plug,
  Clock,
  LayoutDashboard,
  LogOut,
  Plus
} from 'lucide-react';
import toast from 'react-hot-toast';
import { useAuth } from '../../hooks/useAuth';
import { useQuery } from '@tanstack/react-query';
import { foldersApi } from '../../lib/itemsApi';

export const Sidebar = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const { user, logout } = useAuth();
  
  const currentFolder = searchParams.get('folder');

  const { data: folders = [] } = useQuery({
    queryKey: ['folders'],
    queryFn: () => foldersApi.getFolders(false),
  });

  const handleLogout = () => {
    logout();
    toast.success('Đã đăng xuất');
    navigate('/login', { replace: true });
  };

  const handleFolderClick = (folderId: string | null) => {
    if (location.pathname !== '/' && location.pathname !== '/kanban') {
      navigate(folderId ? `/?folder=${folderId}` : '/');
    } else {
      const newParams = new URLSearchParams(searchParams);
      if (!folderId) {
        newParams.delete('folder');
      } else {
        newParams.set('folder', folderId);
      }
      navigate(`${location.pathname}?${newParams.toString()}`);
    }
  };

  const navItemClass = (isActive: boolean) => 
    `flex items-center gap-2.5 w-full px-[10px] py-[9px] rounded-lg border-none cursor-pointer text-[14px] font-inherit transition-colors ${
      isActive 
        ? 'font-semibold bg-indigo-50 text-indigo-600' 
        : 'font-medium bg-transparent text-slate-500 hover:bg-slate-100'
    }`;

  return (
    <aside className="w-[232px] bg-white border-r border-slate-200 flex flex-col h-full shrink-0 px-3 py-4">
      {/* ── Top Branding ── */}
      <div className="flex items-center gap-2.5 px-2 pt-1 pb-[18px]">
        <div className="w-[30px] h-[30px] rounded-lg bg-indigo-600 flex items-center justify-center text-white font-bold text-[15px] shrink-0">
          W
        </div>
        <span className="text-[15px] font-semibold text-slate-900">
          Workspace Hub
        </span>
      </div>

      {/* ── Main Nav ── */}
      <nav className="flex flex-col gap-0.5">
        <NavLink to="/" className={({ isActive }) => navItemClass(isActive && location.pathname === '/')}>
          <Inbox className="w-[18px] h-[18px] shrink-0" />
          <span className="flex-1 text-left">Inbox</span>
        </NavLink>

        <NavLink to="/kanban" className={({ isActive }) => navItemClass(isActive)}>
          <Kanban className="w-[18px] h-[18px] shrink-0" />
          <span className="flex-1 text-left">Bảng Kanban</span>
        </NavLink>

        <NavLink to="/integrations" className={({ isActive }) => navItemClass(isActive)}>
          <Plug className="w-[18px] h-[18px] shrink-0" />
          <span className="flex-1 text-left">Kết nối dịch vụ</span>
        </NavLink>

        <NavLink to="/scheduled-emails" className={({ isActive }) => navItemClass(isActive)}>
          <Clock className="w-[18px] h-[18px] shrink-0" />
          <span className="flex-1 text-left">Email hẹn giờ</span>
        </NavLink>

        {user?.role === 'Admin' && (
          <NavLink to="/admin" className={({ isActive }) => navItemClass(isActive)}>
            <LayoutDashboard className="w-[18px] h-[18px] shrink-0" />
            <span className="flex-1 text-left">Quản trị</span>
          </NavLink>
        )}
      </nav>

      {/* ── Folders Section ── */}
      <div className="flex items-center justify-between mx-[10px] mt-6 mb-2">
        <span className="text-[11px] font-semibold tracking-[0.04em] uppercase text-slate-400">
          Thư mục
        </span>
        <button 
          aria-label="Thư mục mới" 
          className="p-0.5 rounded-md text-slate-400 hover:bg-slate-100 hover:text-slate-900 transition-colors flex"
          onClick={() => toast('Tính năng Thư mục mới đang được phát triển')}
        >
          <Plus className="w-4 h-4" />
        </button>
      </div>

      <div className="flex flex-col gap-0.5 overflow-y-auto flex-1 min-h-0 hide-scrollbar">
        <button
          onClick={() => handleFolderClick(null)}
          className={navItemClass(!currentFolder)}
        >
          <span className="w-2 h-2 rounded-full shrink-0 bg-slate-400" />
          <span className="flex-1 text-left truncate">Tất cả</span>
        </button>
        
        {folders.map(folder => (
          <button
            key={folder.id}
            onClick={() => handleFolderClick(folder.id)}
            className={navItemClass(currentFolder === folder.id)}
          >
            <span 
              className="w-2 h-2 rounded-full shrink-0" 
              style={{ backgroundColor: folder.color || '#94a3b8' }} 
            />
            <span className="flex-1 text-left truncate">{folder.name}</span>
          </button>
        ))}
      </div>

      {/* ── Bottom User Profile ── */}
      <div className="mt-4 pt-3 border-t border-slate-200">
        <div className="flex items-center gap-2.5 px-1">
          <div className="w-[34px] h-[34px] rounded-full bg-indigo-50 text-indigo-600 flex items-center justify-center text-[13px] font-semibold shrink-0">
            {user?.fullName ? user.fullName.charAt(0).toUpperCase() : 'U'}
          </div>
          <div className="flex-1 min-w-0">
            <div className="truncate text-[13.5px] font-semibold text-slate-900">
              {user?.fullName ?? 'Người dùng'}
            </div>
            <div className="truncate text-[12px] text-slate-500">
              {user?.email}
            </div>
          </div>
          <button
            onClick={handleLogout}
            aria-label="Đăng xuất"
            title="Đăng xuất"
            className="shrink-0 rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-900 transition-colors flex"
          >
            <LogOut className="w-[18px] h-[18px]" />
          </button>
        </div>
      </div>
    </aside>
  );
};
