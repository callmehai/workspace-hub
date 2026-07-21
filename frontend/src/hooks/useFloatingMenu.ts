import { useMemo } from 'react';
import {
  autoUpdate,
  flip,
  offset,
  shift,
  size,
  useDismiss,
  useFloating,
  useInteractions,
  useRole,
  FloatingPortal,
  type Placement,
} from '@floating-ui/react';
import { PORTAL_MENU_Z_INDEX } from '../lib/portalZIndex';

export interface UseFloatingMenuOptions {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Vị trí ưu tiên — middleware `flip` tự đổi khi thiếu chỗ. */
  placement?: Placement;
  /** Menu rộng bằng nút trigger (mặc định true — Select). */
  matchWidth?: boolean;
  /** Chiều rộng tối thiểu (vd FriendMultiSelect). */
  minWidth?: number;
  /** Chiều rộng cố định (vd DatePicker, TimePicker). */
  width?: number;
  /** ARIA role cho menu/popover. */
  role?: 'listbox' | 'menu' | 'dialog';
}

/**
 * Hook positioning chuẩn cho dropdown/menu/popover portal (Floating UI).
 * Dùng chung Select, FriendMultiSelect, DatePicker, TimePicker, DateTimePicker.
 */
export function useFloatingMenu({
  open,
  onOpenChange,
  placement = 'bottom-start',
  matchWidth = true,
  minWidth,
  width,
  role = 'listbox',
}: UseFloatingMenuOptions) {
  const middleware = useMemo(
    () => [
      offset(6),
      flip({ padding: 8 }),
      shift({ padding: 8 }),
      ...(matchWidth || minWidth || width
        ? [
            size({
              apply({ rects, elements }) {
                const resolvedWidth = width ?? (matchWidth ? rects.reference.width : 0);
                const min = Math.max(minWidth ?? 0, resolvedWidth);
                if (min > 0) {
                  Object.assign(elements.floating.style, {
                    width: `${min}px`,
                    minWidth: minWidth ? `${minWidth}px` : undefined,
                  });
                }
              },
            }),
          ]
        : []),
    ],
    [matchWidth, minWidth, width],
  );

  const { refs, floatingStyles, context } = useFloating({
    open,
    onOpenChange,
    placement,
    middleware,
    whileElementsMounted: autoUpdate,
    strategy: 'fixed',
  });

  const dismiss = useDismiss(context, { outsidePressEvent: 'mousedown' });
  const floatingRole = useRole(context, { role });
  const { getReferenceProps, getFloatingProps } = useInteractions([dismiss, floatingRole]);

  return {
    refs,
    floatingStyles: {
      ...floatingStyles,
      zIndex: PORTAL_MENU_Z_INDEX,
    },
    getReferenceProps,
    getFloatingProps,
    FloatingPortal,
  };
}
