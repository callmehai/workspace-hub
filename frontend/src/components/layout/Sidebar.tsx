import React, { useState, useRef, useEffect } from 'react';
import { NavLink, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import {
  Layers,
  Plug,
  Clock,
  Send,
  LayoutDashboard,
  LogOut,
  Plus,
  MoreHorizontal,
  Pencil,
  Trash2,
  X
} from 'lucide-react';
import toast from 'react-hot-toast';
import { useAuth } from '../../hooks/useAuth';
import { useI18n } from '../../hooks/useI18n';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { foldersApi } from '../../lib/itemsApi';
import { FolderModal } from '../folders/FolderModal';
import { ConfirmDialog } from '../ConfirmDialog';
import { INTEGRATION_TABS } from '../../lib/itemVisuals';
import type { FolderResponse } from '../../types/items';
import { handleApiError } from '../../lib/errorUtils';

interface SidebarProps {
  mobileOpen?: boolean;
  onMobileClose?: () => void;
}
export const Sidebar = ({ mobileOpen = false, onMobileClose }: SidebarProps) => {
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const { user, logout } = useAuth();
  const { t } = useI18n();
  const queryClient = useQueryClient();

  const currentFolder = searchParams.get('folder');
  const currentType = searchParams.get('type'); // scope integration (?type=Email|Event|File|Ticket)

  // Folder = CONTEXT (không phải filter). Context có 2 view: Danh sách (/) và Bảng (/kanban).
  // Đổi context giữ nguyên view đang xem; đổi view (trong page) giữ nguyên context.
  const isItemsView = location.pathname === '/' || location.pathname === '/kanban';
  const viewPath = isItemsView ? location.pathname : '/';

  const [isFolderModalOpen, setIsFolderModalOpen] = useState(false);
  const [editingFolder, setEditingFolder] = useState<FolderResponse | undefined>();
  const [activeMenuId, setActiveMenuId] = useState<string | null>(null);
  const [folderToDelete, setFolderToDelete] = useState<FolderResponse | null>(null);

  const menuRef = useRef<HTMLDivElement>(null);

  const { data: folders = [] } = useQuery({
    queryKey: ['folders'],
    queryFn: () => foldersApi.getFolders(false),
  });

  const deleteMutation = useMutation({
    mutationFn: foldersApi.deleteFolder,
    onSuccess: (_, deletedId) => {
      toast.success(t('sidebar.folderDeleted'));
      queryClient.invalidateQueries({ queryKey: ['folders'] });
      // Đang đứng trong folder vừa xoá → quay về "Tất cả mục" (giữ view)
      if (currentFolder === deletedId) navigate(viewPath);
    },
    onError: () => {
      toast.error(t('sidebar.folderDeleteFail'));
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
    toast.success(t('nav.loggedOut'));
    navigate('/login', { replace: true });
  };

  const handleFolderClick = (folderId: string | null) => {
    navigate(folderId ? `${viewPath}?folder=${folderId}` : viewPath);
    onMobileClose?.();
  };

  // Chọn 1 nguồn (integration) → scope trang theo loại đó. Loại trừ lẫn nhau với folder.
  const handleSourceClick = (type: string) => {
    navigate(`${viewPath}?type=${type}`);
    onMobileClose?.();
  };

  // "Tất cả mục" active khi KHÔNG chọn folder VÀ KHÔNG chọn nguồn nào.
  const allItemsActive = isItemsView && !currentFolder && !currentType;
  const isFolderActive = (id: string) => isItemsView && currentFolder === id;
  const isSourceActive = (type: string) => isItemsView && !currentFolder && currentType === type;

  const assignItemMutation = useMutation({
    mutationFn: ({ folderId, itemId }: { folderId: string; itemId: string }) =>
      foldersApi.addItemToFolder(folderId, { itemId }),
    onSuccess: (_, variables) => {
      toast.success(t('sidebar.itemAssigned'));
      queryClient.invalidateQueries({ queryKey: ['items'] });
      queryClient.invalidateQueries({ queryKey: ['item', variables.itemId] });
    },
    onError: (err) => {
      handleApiError(err, 'Lỗi gán thư mục');
    }
  });

  const assignItemsBulkMutation = useMutation({
    mutationFn: ({ folderId, itemIds }: { folderId: string; itemIds: string[] }) =>
      foldersApi.addItemsToFolderBulk(folderId, itemIds),
    onSuccess: (_, variables) => {
      toast.success(`Đã gán ${variables.itemIds.length} mục vào thư mục`);
      queryClient.invalidateQueries({ queryKey: ['items'] });
      queryClient.invalidateQueries({ queryKey: ['item'] });
    },
    onError: (err) => {
      handleApiError(err, 'Lỗi gán thư mục');
    }
  });

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    e.currentTarget.classList.add('bg-indigo-50', 'dark:bg-brand-500/10');
  };

  const handleDragLeave = (e: React.DragEvent) => {
    e.currentTarget.classList.remove('bg-indigo-50', 'dark:bg-brand-500/10');
  };
  const handleDrop = (e: React.DragEvent, folderId: string) => {
    e.preventDefault();
    e.currentTarget.classList.remove('bg-indigo-50', 'dark:bg-brand-500/10');

    const itemIdsStr = e.dataTransfer.getData('itemIds');
    const itemId = e.dataTransfer.getData('itemId');

    if (itemIdsStr) {
      try {
        const itemIds = JSON.parse(itemIdsStr);
        if (Array.isArray(itemIds) && itemIds.length > 0) {
          assignItemsBulkMutation.mutate({ folderId, itemIds });
        }
      } catch {
        console.error("Failed to parse dragged items");
      }
    } else if (itemId) {
      assignItemMutation.mutate({ folderId, itemId });
    }
  };

  const navItemClass = (isActive: boolean) =>
    `flex items-center gap-2.5 w-full px-[10px] py-[9px] rounded-lg border-none cursor-pointer text-[14px] font-inherit transition-colors ${isActive
      ? 'font-semibold bg-brand-50 text-brand-600 dark:bg-brand-500/15 dark:text-brand-300'
      : 'font-medium bg-transparent text-slate-500 hover:bg-slate-100 dark:text-slate-400 dark:hover:bg-slate-800'
    }`;

  return (
    <>
      <aside
        className={`
          fixed inset-y-0 left-0 z-50 w-[232px] shrink-0 flex flex-col h-full px-3 py-4
          bg-white border-r border-slate-200 dark:bg-slate-900 dark:border-slate-800
          transform transition-transform duration-200 ease-out
          ${mobileOpen ? 'translate-x-0' : '-translate-x-full'}
          lg:static lg:translate-x-0 lg:flex
        `}
      >
        {/* ── Top Branding ── */}
        <div className="flex items-center justify-between gap-2 px-2 pt-1 pb-[18px]">
          <div className="flex items-center gap-2.5 min-w-0">
            <div className="w-[30px] h-[30px] rounded-lg bg-brand-600 flex items-center justify-center text-white font-bold text-[15px] shrink-0">
              W
            </div>
            <span className="text-[15px] font-semibold text-slate-900 dark:text-slate-100 truncate">
              {t('common.appName')}
            </span>
          </div>
          <button
            type="button"
            onClick={onMobileClose}
            aria-label={t('nav.closeMenu')}
            className="lg:hidden flex h-8 w-8 shrink-0 items-center justify-center rounded-md text-slate-500 hover:bg-slate-100 dark:text-slate-400 dark:hover:bg-slate-800 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* ── Main Nav ── */}
        <nav className="flex flex-col gap-0.5">
          {/* Context mặc định: mọi item. View (Danh sách/Bảng) đổi trong page, giữ nguyên khi đổi context. */}
          <button onClick={() => handleFolderClick(null)} className={navItemClass(allItemsActive)}>
            <Layers className="w-[18px] h-[18px] shrink-0" />
            <span className="flex-1 text-left">{t('nav.allItems')}</span>
          </button>

          {/* ── Nguồn (integration) — mỗi tab scope 1 loại; filter riêng mở trong page ── */}
          {INTEGRATION_TABS.map(({ type, labelKey, Icon }) => (
            <button
              key={type}
              onClick={() => handleSourceClick(type)}
              className={navItemClass(isSourceActive(type))}
            >
              <Icon className="w-[18px] h-[18px] shrink-0" strokeWidth={2} />
              <span className="flex-1 text-left">{t(labelKey)}</span>
            </button>
          ))}

          <div className="my-1.5 border-t border-slate-100 dark:border-slate-800" />

          <NavLink to="/integrations" onClick={() => onMobileClose?.()} className={({ isActive }) => navItemClass(isActive)}>
            <Plug className="w-[18px] h-[18px] shrink-0" />
            <span className="flex-1 text-left">{t('nav.integrations')}</span>
          </NavLink>

          <NavLink to="/send-email" onClick={() => onMobileClose?.()} className={({ isActive }) => navItemClass(isActive)}>
            <Send className="w-[18px] h-[18px] shrink-0" />
            <span className="flex-1 text-left">{t('nav.sendEmail')}</span>
          </NavLink>

          <NavLink to="/scheduled-emails" onClick={() => onMobileClose?.()} className={({ isActive }) => navItemClass(isActive)}>
            <Clock className="w-[18px] h-[18px] shrink-0" />
            <span className="flex-1 text-left">{t('nav.scheduledEmails')}</span>
          </NavLink>

          {user?.role === 'Admin' && (
            <NavLink to="/admin" onClick={() => onMobileClose?.()} className={({ isActive }) => navItemClass(isActive)}>
              <LayoutDashboard className="w-[18px] h-[18px] shrink-0" />
              <span className="flex-1 text-left">{t('nav.admin')}</span>
            </NavLink>
          )}
        </nav>

        {/* ── Folders Section ── */}
        <div className="flex items-center justify-between mx-[10px] mt-6 mb-2">
          <span className="text-[11px] font-semibold tracking-[0.04em] uppercase text-slate-400 dark:text-slate-500">
            {t('nav.folders')}
          </span>
          <button
            aria-label={t('nav.newFolder')}
            className="p-0.5 rounded-md text-slate-400 hover:bg-slate-100 hover:text-slate-900 dark:text-slate-500 dark:hover:bg-slate-800 dark:hover:text-slate-100 transition-colors flex"
            onClick={() => {
              setEditingFolder(undefined);
              setIsFolderModalOpen(true);
            }}
          >
            <Plus className="w-4 h-4" />
          </button>
        </div>

        <div className="flex flex-col gap-0.5 overflow-y-auto flex-1 min-h-0 hide-scrollbar">
          {folders.length === 0 && (
            <div className="px-[10px] py-2 text-[12px] text-slate-400 dark:text-slate-500 leading-relaxed">
              {t('nav.noFolders')}
            </div>
          )}

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
                className={navItemClass(isFolderActive(folder.id))}
              >
                <span
                  className="w-2 h-2 rounded-full shrink-0"
                  style={{ backgroundColor: folder.color || '#94a3b8' }}
                />
                <span className="flex-1 text-left truncate">{folder.name}</span>
                <span className="text-[11px] tabular-nums text-slate-400 dark:text-slate-500 shrink-0 group-hover:opacity-0 transition-opacity">
                  {folder.itemCount}
                </span>
              </button>

              <button
                data-folder-toggle
                className={`absolute right-2 p-1 rounded hover:bg-slate-200 text-slate-400 hover:text-slate-700 dark:hover:bg-slate-700 dark:text-slate-500 dark:hover:text-slate-200 transition-colors ${activeMenuId === folder.id ? 'opacity-100' : 'opacity-0 group-hover:opacity-100'
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
                  className="absolute right-0 top-8 w-32 bg-white dark:bg-slate-800 rounded-lg shadow-lg border border-slate-100 dark:border-slate-700 py-1 z-50 text-sm"
                >
                  <button
                    className="w-full text-left px-3 py-1.5 hover:bg-slate-50 dark:hover:bg-slate-700 text-slate-700 dark:text-slate-200 flex items-center gap-2"
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
                    className="w-full text-left px-3 py-1.5 hover:bg-slate-50 dark:hover:bg-slate-700 text-rose-600 dark:text-rose-400 flex items-center gap-2"
                    onClick={() => {
                      setFolderToDelete(folder);
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
        <div className="mt-4 pt-3 border-t border-slate-200 dark:border-slate-800">
          <div className="flex items-center gap-2.5 px-1">
            <NavLink
              to="/profile"
              onClick={() => onMobileClose?.()}
              className="flex flex-1 min-w-0 items-center gap-2.5 rounded-lg p-1 -m-1 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
            >
              <div className="w-[34px] h-[34px] rounded-full bg-brand-50 text-brand-600 dark:bg-slate-800 dark:text-brand-300 flex items-center justify-center text-[13px] font-semibold shrink-0 overflow-hidden">
                {user?.avatarUrl ? (
                  <img src={user.avatarUrl} alt={user.fullName} className="h-full w-full object-cover" />
                ) : user?.fullName ? (
                  user.fullName.charAt(0).toUpperCase()
                ) : (
                  'U'
                )}
              </div>
              <div className="flex-1 min-w-0">
                <div className="truncate text-[13.5px] font-semibold text-slate-900 dark:text-slate-100">
                  {user?.fullName ?? t('nav.user')}
                </div>
                <div className="truncate text-[12px] text-slate-500 dark:text-slate-400">
                  {user?.email}
                </div>
              </div>
            </NavLink>
            <button
              onClick={handleLogout}
              aria-label={t('nav.logout')}
              title={t('nav.logout')}
              className="shrink-0 rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-900 dark:text-slate-500 dark:hover:bg-slate-800 dark:hover:text-slate-100 transition-colors flex"
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

      <ConfirmDialog
        open={folderToDelete !== null}
        tone="danger"
        title={folderToDelete?.name}
        message={t('sidebar.confirmDeleteFolder')}
        confirmLabel={t('common.delete')}
        loading={deleteMutation.isPending}
        onConfirm={() => {
          if (folderToDelete) {
            deleteMutation.mutate(folderToDelete.id, { onSettled: () => setFolderToDelete(null) });
          }
        }}
        onCancel={() => setFolderToDelete(null)}
      />
    </>
  );
};
