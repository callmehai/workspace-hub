import { useState, useEffect, useMemo, useCallback, useRef } from 'react';
import { ChevronDown, Check, Search, Star, X } from 'lucide-react';
import { useI18n } from '../hooks/useI18n';
import { useFloatingMenu } from '../hooks/useFloatingMenu';
import type { FriendDto } from '../lib/friendsApi';

interface FriendMultiSelectProps {
  friends: FriendDto[];
  /** userId của những người đang được chọn. */
  value: string[];
  onChange: (userIds: string[]) => void;
  disabled?: boolean;
  className?: string;
}

/** Bỏ dấu tiếng Việt để search "hai" khớp "Hải". */
const normalize = (s: string) =>
  s.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLowerCase();

/**
 * Dropdown chọn NHIỀU bạn bè — có ô tìm kiếm + checkbox + nút chọn nhanh toàn bộ bạn thân.
 * Thay cho native <select> (chỉ chọn được 1 người, không search được).
 */
export function FriendMultiSelect({ friends, value, onChange, disabled, className = '' }: FriendMultiSelectProps) {
  const { t } = useI18n();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const searchRef = useRef<HTMLInputElement>(null);

  /** Đóng menu + xoá từ khoá tìm kiếm (lần mở sau bắt đầu với danh sách đầy đủ). */
  const closeMenu = useCallback(() => {
    setOpen(false);
    setQuery('');
  }, []);

  const handleOpenChange = useCallback((next: boolean) => {
    if (!next) closeMenu();
    else setOpen(true);
  }, [closeMenu]);

  const { refs, floatingStyles, getReferenceProps, getFloatingProps, FloatingPortal } = useFloatingMenu({
    open,
    onOpenChange: handleOpenChange,
    matchWidth: true,
    minWidth: 260,
  });

  // Mở dropdown → focus ngay ô tìm kiếm để gõ luôn.
  useEffect(() => {
    if (open) searchRef.current?.focus();
  }, [open]);

  const filtered = useMemo(() => {
    const q = normalize(query.trim());
    const sorted = [...friends].sort(
      (a, b) => Number(b.myTier === 'CloseFriend') - Number(a.myTier === 'CloseFriend'),
    );
    if (!q) return sorted;
    return sorted.filter(
      (f) => normalize(f.fullName).includes(q) || normalize(f.email).includes(q),
    );
  }, [friends, query]);

  const closeFriends = useMemo(() => friends.filter((f) => f.myTier === 'CloseFriend'), [friends]);
  const allCloseSelected =
    closeFriends.length > 0 && closeFriends.every((f) => value.includes(f.userId));

  const toggle = (userId: string) => {
    onChange(value.includes(userId) ? value.filter((id) => id !== userId) : [...value, userId]);
  };

  const toggleAllCloseFriends = () => {
    if (allCloseSelected) {
      const closeIds = new Set(closeFriends.map((f) => f.userId));
      onChange(value.filter((id) => !closeIds.has(id)));
    } else {
      onChange([...new Set([...value, ...closeFriends.map((f) => f.userId)])]);
    }
  };

  const selectedFriends = friends.filter((f) => value.includes(f.userId));

  const triggerLabel =
    selectedFriends.length === 0
      ? t('share.selectFriends')
      : selectedFriends.length === 1
        ? selectedFriends[0].fullName
        : t('share.friendsSelected').replace('{count}', String(selectedFriends.length));

  return (
    <div className="relative">
      <button
        ref={refs.setReference}
        type="button"
        disabled={disabled}
        {...getReferenceProps({
          onClick: () => {
            if (disabled) return;
            if (open) closeMenu();
            else setOpen(true);
          },
        })}
        className={`w-full flex items-center justify-between gap-2 px-3 border rounded-lg text-sm bg-white dark:bg-slate-800 transition-colors disabled:opacity-60 disabled:cursor-not-allowed ${
          open
            ? 'border-brand-500 ring-2 ring-brand-500/20'
            : 'border-gray-300 hover:border-gray-400 dark:border-slate-700 dark:hover:border-slate-600'
        } ${className}`}
      >
        <span
          className={`min-w-0 truncate text-left ${
            selectedFriends.length ? 'text-gray-800 dark:text-slate-100' : 'text-gray-400 dark:text-slate-500'
          }`}
        >
          {triggerLabel}
        </span>
        <ChevronDown
          className={`w-4 h-4 text-gray-400 dark:text-slate-500 shrink-0 transition-transform ${open ? 'rotate-180' : ''}`}
        />
      </button>

      {/* Chip những người đã chọn — bấm X để bỏ chọn nhanh */}
      {selectedFriends.length > 0 && (
        <div className="flex flex-wrap gap-1.5 mt-2">
          {selectedFriends.map((f) => (
            <span
              key={f.userId}
              className="inline-flex items-center gap-1 pl-2 pr-1 py-0.5 rounded-full text-xs bg-brand-50 text-brand-700 border border-brand-200 dark:bg-brand-500/15 dark:text-brand-300 dark:border-brand-500/30"
            >
              <span className="truncate max-w-[140px]">{f.fullName}</span>
              <button
                type="button"
                onClick={() => toggle(f.userId)}
                className="p-0.5 rounded-full hover:bg-brand-100 dark:hover:bg-brand-500/25"
              >
                <X className="w-3 h-3" />
              </button>
            </span>
          ))}
        </div>
      )}

      {open && (
        <FloatingPortal>
          <div
            ref={refs.setFloating}
            style={floatingStyles}
            {...getFloatingProps()}
            className="bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-xl shadow-xl overflow-hidden"
          >
            {/* Ô tìm kiếm */}
            <div className="p-2 border-b border-gray-100 dark:border-slate-700">
              <div className="relative">
                <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-400 dark:text-slate-500" />
                <input
                  ref={searchRef}
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  placeholder={t('share.searchFriends')}
                  className="w-full h-8 pl-8 pr-2 rounded-lg border border-gray-200 dark:border-slate-600 bg-white dark:bg-slate-900 text-sm text-gray-800 dark:text-slate-100 placeholder:text-gray-400 dark:placeholder:text-slate-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
                />
              </div>
            </div>

            {/* Chọn nhanh toàn bộ bạn thân */}
            {closeFriends.length > 0 && (
              <button
                type="button"
                onClick={toggleAllCloseFriends}
                className="w-full flex items-center gap-2 px-3 py-2 text-sm text-left border-b border-gray-100 dark:border-slate-700 text-amber-700 dark:text-amber-400 hover:bg-amber-50 dark:hover:bg-amber-500/10 transition-colors"
              >
                <Star className={`w-4 h-4 shrink-0 ${allCloseSelected ? 'fill-amber-400 text-amber-400' : ''}`} />
                <span className="truncate">
                  {allCloseSelected ? t('share.deselectCloseFriends') : t('share.selectCloseFriends')} (
                  {closeFriends.length})
                </span>
              </button>
            )}

            {/* Danh sách bạn bè */}
            <div className="max-h-56 overflow-auto py-1">
              {filtered.length === 0 ? (
                <div className="px-3 py-3 text-sm text-gray-400 dark:text-slate-500 text-center">
                  {query ? t('share.noFriendsFound') : t('share.noFriendsAvailable')}
                </div>
              ) : (
                filtered.map((f) => {
                  const checked = value.includes(f.userId);
                  return (
                    <button
                      key={f.userId}
                      type="button"
                      onClick={() => toggle(f.userId)}
                      className="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-left transition-colors text-gray-700 hover:bg-gray-50 dark:text-slate-300 dark:hover:bg-slate-700"
                    >
                      <span
                        className={`w-4 h-4 shrink-0 rounded border flex items-center justify-center transition-colors ${
                          checked
                            ? 'bg-brand-600 border-brand-600 text-white'
                            : 'border-gray-300 dark:border-slate-600'
                        }`}
                      >
                        {checked && <Check className="w-3 h-3" />}
                      </span>
                      <span className="min-w-0 flex-1">
                        <span className="flex items-center gap-1.5">
                          <span className="truncate font-medium">{f.fullName}</span>
                          {f.myTier === 'CloseFriend' && (
                            <Star className="w-3 h-3 shrink-0 fill-amber-400 text-amber-400" />
                          )}
                        </span>
                        <span className="block truncate text-xs text-gray-400 dark:text-slate-500">{f.email}</span>
                      </span>
                    </button>
                  );
                })
              )}
            </div>
          </div>
        </FloatingPortal>
      )}
    </div>
  );
}
