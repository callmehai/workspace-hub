import React, { useState, useRef, useEffect } from 'react';
import { NavLink, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import {
  Inbox,
  Kanban,
  Plug,
  Clock,
  LayoutDashboard,
  LogOut,
  Plus,
  MoreHorizontal,
  Pencil,
  Trash2
} from 'lucide-react';
import toast from 'react-hot-toast';
import { useAuth } from '../../hooks/useAuth';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { foldersApi } from '../../lib/itemsApi';
import { FolderModal } from '../folders/FolderModal';
import type { FolderResponse } from '../../types/items';
import { handleApiError } from '../../lib/errorUtils';

export const Sidebar = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const { user, logout } = useAuth();
  const queryClient = useQueryClient();
  
  const currentFolder = searchParams.get('folder');

  const [isFolderModalOpen, setIsFolderModalOpen] = useState(false);
  const [editingFolder, setEditingFolder] = useState<FolderResponse | undefined>();
  const [activeMenuId, setActiveMenuId] = useState<string | null>(null);
  
  const menuRef = useRef<HTMLDivElement>(null);

  const { data: folders = [] } = useQuery({
    queryKey: ['folders'],
    queryFn: () => foldersApi.getFolders(false),
  });

  const deleteMutation = useMutation({
    mutationFn: foldersApi.deleteFolder,
    onSuccess: () => {
      toast.success('Đã xóa thư mục');
      queryClient.invalidateQueries({ queryKey: ['folders'] });
    },
    onError: () => {
      toast.error('Lỗi khi xóa thư mục');
    }
  });

  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      const target = event.target as Element;
      if (target.closest('[data-folder-toggle]')) return;
      if (menuRef.current && !menuRef.current.contains(target)) {
        setActiveMenuId(null);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

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

  const assignItemMutation = useMutation({
    mutationFn: ({ folderId, itemId }: { folderId: string; itemId: string }) => 
      foldersApi.addItemToFolder(folderId, { itemId }),
    onSuccess: () => {
      toast.success('Đã gán mục vào thư mục');
      queryClient.invalidateQueries({ queryKey: ['items'] });
    },
    onError: (err) => {
      handleApiError(err, 'Lỗi gán thư mục');
    }
  });

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    e.currentTarget.classList.add('bg-indigo-50');
  };

  const handleDragLeave = (e: React.DragEvent) => {
    e.currentTarget.classList.remove('bg-indigo-50');
  };

  const handleDrop = (e: React.DragEvent, folderId: string) => {
    e.preventDefault();
    e.currentTarget.classList.remove('bg-indigo-50');
    const itemId = e.dataTransfer.getData('itemId');
    if (itemId) {
      assignItemMutation.mutate({ folderId, itemId });
    }
  };

  const navItemClass = (isActive: boolean) => 
    `flex items-center gap-2.5 w-full px-[10px] py-[9px] rounded-lg border-none cursor-pointer text-[14px] font-inherit transition-colors ${
      isActive 
        ? 'font-semibold bg-indigo-50 text-indigo-600' 
        : 'font-medium bg-transparent text-slate-500 hover:bg-slate-100'
    }`;

  return (
    <>
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
          <NavLink to="/" end className={({ isActive }) => navItemClass(isActive)}>
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
            onClick={() => {
              setEditingFolder(undefined);
              setIsFolderModalOpen(true);
            }}
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
            <div 
              key={folder.id} 
              className="relative group flex items-center rounded-lg"
              onDragOver={handleDragOver}
              onDragLeave={handleDragLeave}
              onDrop={(e) => handleDrop(e, folder.id)}
            >
              <button
                onClick={() => handleFolderClick(folder.id)}
                className={navItemClass(currentFolder === folder.id)}
              >
                <span 
                  className="w-2 h-2 rounded-full shrink-0" 
                  style={{ backgroundColor: folder.color || '#94a3b8' }} 
                />
                <span className="flex-1 text-left truncate">{folder.name}</span>
              </button>
              
              <button
                data-folder-toggle
                className={`absolute right-2 p-1 rounded hover:bg-slate-200 text-slate-400 hover:text-slate-700 transition-colors ${
                  activeMenuId === folder.id ? 'opacity-100' : 'opacity-0 group-hover:opacity-100'
                }`}
                onClick={(e) => {
                  e.stopPropagation();
                  setActiveMenuId(activeMenuId === folder.id ? null : folder.id);
                }}
              >
                <MoreHorizontal className="w-4 h-4" />
              </button>
              
              {activeMenuId === folder.id && (
                <div 
                  ref={menuRef} 
                  className="absolute right-0 top-8 w-32 bg-white rounded-lg shadow-lg border border-slate-100 py-1 z-50 text-sm"
                >
                  <button
                    className="w-full text-left px-3 py-1.5 hover:bg-slate-50 text-slate-700 flex items-center gap-2"
                    onClick={() => {
                      setEditingFolder(folder);
                      setIsFolderModalOpen(true);
                      setActiveMenuId(null);
                    }}
                  >
                    <Pencil className="w-3.5 h-3.5" />
                    Sửa
                  </button>
                  <button
                    className="w-full text-left px-3 py-1.5 hover:bg-slate-50 text-rose-600 flex items-center gap-2"
                    onClick={() => {
                      if (window.confirm('Bạn có chắc chắn muốn xóa thư mục này?')) {
                        deleteMutation.mutate(folder.id);
                      }
                      setActiveMenuId(null);
                    }}
                  >
                    <Trash2 className="w-3.5 h-3.5" />
                    Xóa
                  </button>
                </div>
              )}
            </div>
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

      <FolderModal
        isOpen={isFolderModalOpen}
        onClose={() => setIsFolderModalOpen(false)}
        folder={editingFolder}
      />
    </>
  );
};
