/**
 * i18n dictionary — VI (mặc định) + EN.
 *
 * Nguyên tắc:
 *  - Key phẳng dạng `namespace.key` để dễ grep và type-safe (kiểu `TranslationKey` = keyof vi).
 *  - `vi` là nguồn chân lý về tập key. `en` PHẢI có đủ mọi key của `vi` (ép ở kiểu `Record<TranslationKey, string>`).
 *  - Nội suy biến: `t('inbox.count', { n: 5 })` thay `{n}` trong chuỗi.
 *
 * Phạm vi hiện tại (SCRUM-73): shell + auth + profile đủ 100%, các page chính (Inbox/Kanban/Integrations)
 * dịch phần nhãn chính — phần còn lại mở rộng dần (thêm key vào cả `vi` và `en`).
 */

export const vi = {
  // ── common ──
  'common.appName': 'Workspace Hub',
  'common.loading': 'Đang tải…',
  'common.save': 'Lưu',
  'common.saving': 'Đang lưu…',
  'common.cancel': 'Huỷ',
  'common.close': 'Đóng',
  'common.retry': 'Thử lại',
  'common.or': 'hoặc',
  'common.comingSoon': 'Sắp có',

  // ── nav / sidebar ──
  'nav.allItems': 'Tất cả mục',
  'nav.integrations': 'Kết nối dịch vụ',
  'nav.sendEmail': 'Gửi email',
  'nav.scheduledEmails': 'Email hẹn giờ',
  'nav.admin': 'Quản trị',
  'nav.profile': 'Hồ sơ',
  'nav.folders': 'Thư mục',
  'nav.newFolder': 'Thư mục mới',
  'nav.noFolders': 'Chưa có thư mục. Tạo thư mục để gom item theo dự án / khách hàng.',
  'nav.logout': 'Đăng xuất',
  'nav.loggedOut': 'Đã đăng xuất',
  'nav.user': 'Người dùng',

  // ── header ──
  'header.search': 'Tìm kiếm trong mọi công cụ…',
  'header.connect': 'Kết nối dịch vụ',
  'header.notifications': 'Thông báo',

  // ── theme / language ──
  'theme.toLight': 'Chuyển sang giao diện Sáng',
  'theme.toDark': 'Chuyển sang giao diện Tối',
  'theme.light': 'Sáng',
  'theme.dark': 'Tối',
  'lang.label': 'Ngôn ngữ',
  'lang.switchTo': 'Tiếng Anh',

  // ── login ──
  'login.title': 'Đăng nhập',
  'login.subtitle': 'Chào mừng quay lại Workspace Hub của bạn.',
  'login.email': 'Email',
  'login.password': 'Mật khẩu',
  'login.passwordPlaceholder': 'Tối thiểu 8 ký tự',
  'login.submit': 'Đăng nhập',
  'login.submitting': 'Đang đăng nhập…',
  'login.success': 'Đăng nhập thành công!',
  'login.failed': 'Đăng nhập thất bại. Vui lòng kiểm tra lại thông tin.',
  'login.noAccount': 'Chưa có tài khoản?',
  'login.registerLink': 'Đăng ký',
  'google.suffix': 'bằng Google',
  'google.redirecting': 'Đang chuyển hướng…',

  // ── register ──
  'register.title': 'Tạo tài khoản',
  'register.subtitle': 'Bắt đầu gom tất cả công việc về một nơi.',
  'register.fullName': 'Họ và tên',
  'register.fullNamePlaceholder': 'Nguyễn Văn A',
  'register.phone': 'Số điện thoại',
  'register.password': 'Mật khẩu',
  'register.confirm': 'Nhập lại mật khẩu',
  'register.confirmPlaceholder': 'Nhập lại mật khẩu',
  'register.submit': 'Đăng ký',
  'register.submitting': 'Đang tạo…',
  'register.failed': 'Đăng ký thất bại (email có thể đã được sử dụng)',
  'register.haveAccount': 'Đã có tài khoản?',
  'register.loginLink': 'Đăng nhập',

  // ── validation ──
  'valid.emailInvalid': 'Email không hợp lệ',
  'valid.passwordMin': 'Mật khẩu phải từ 8 ký tự trở lên',
  'valid.nameRequired': 'Vui lòng nhập họ tên',
  'valid.phoneE164': 'SĐT phải dạng E.164, vd +84901234567',
  'valid.confirmMismatch': 'Mật khẩu nhập lại không khớp',

  // ── profile ──
  'profile.title': 'Hồ sơ của tôi',
  'profile.subtitle': 'Thông tin tài khoản và tuỳ chọn hiển thị.',
  'profile.account': 'Thông tin tài khoản',
  'profile.fullName': 'Họ và tên',
  'profile.email': 'Email',
  'profile.role': 'Vai trò',
  'profile.roleAdmin': 'Quản trị viên',
  'profile.roleUser': 'Người dùng',
  'profile.avatar': 'Ảnh đại diện',
  'profile.changeAvatar': 'Đổi ảnh đại diện',
  'profile.avatarHint': 'Tải ảnh đại diện sẽ khả dụng ở bản cập nhật tới.',
  'profile.preferences': 'Tuỳ chọn hiển thị',
  'profile.appearance': 'Giao diện',
  'profile.language': 'Ngôn ngữ',

  // ── inbox / core (phần nhãn chính) ──
  'inbox.count': '{n} mục',
  'inbox.filtering': 'Đang lọc:',
  'inbox.clearFilters': 'Xoá bộ lọc',
  'inbox.selectAll': 'Chọn tất cả trang này',
  'inbox.loadError': 'Không thể tải dữ liệu.',
  'inbox.emptyFiltered': 'Không có mục nào khớp bộ lọc.',
  'inbox.emptyFolder': 'Thư mục này chưa có mục nào.',
  'inbox.empty': 'Chưa có mục nào.',
  'inbox.range': '{start}–{end} trong {total} mục',
  'integrations.title': 'Kết nối dịch vụ',
  'integrations.subtitle': 'Cấp quyền để Workspace Hub đọc và ghi dữ liệu của bạn.',
  'integrations.bannerTitle': 'Đăng nhập bằng Google ≠ Kết nối dịch vụ.',
  'integrations.bannerBody': 'Đăng nhập chỉ xác thực tài khoản của bạn. Để đồng bộ hai chiều, bạn cần cấp quyền (scope) riêng cho từng dịch vụ bên dưới.',
  'integrations.loadError': 'Không tải được kết nối',
  'integrations.loadErrorHint': 'Vui lòng thử lại sau giây lát.',
  'integrations.connect': 'Kết nối',
  'integrations.reconnect': 'Kết nối lại',
  'integrations.disconnect': 'Ngắt',

  // ── workspace toolbar ──
  'toolbar.folder': 'Thư mục',
  'toolbar.folderPrefix': 'Thư mục · ',
  'toolbar.sync': 'Đồng bộ',
  'toolbar.syncing': 'Đang đồng bộ dữ liệu…',
  'toolbar.syncDone': 'Đồng bộ thành công!',
  'toolbar.list': 'Danh sách',
  'toolbar.board': 'Bảng',
  'toolbar.note': 'Ghi chú',
  'toolbar.event': 'Sự kiện',
  'toolbar.important': 'Quan trọng',
  'toolbar.search': 'Tìm kiếm tiêu đề, nội dung… (không cần gõ dấu)',

  // ── kanban ──
  'kanban.subtitle': 'Bảng Kanban · kéo-thả thẻ để đổi trạng thái',
  'kanban.colInbox': 'Chưa xem',
  'kanban.colDoing': 'Đang xử lý',
  'kanban.colDone': 'Hoàn thành',
  'kanban.loadError': 'Không tải được bảng',
  'kanban.loadErrorHint': 'Mất kết nối tới máy chủ. Vui lòng thử lại.',
  'kanban.dropHere': 'Kéo thẻ vào đây',
  'kanban.loadMore': 'Tải thêm',
  'kanban.loadingMore': 'Đang tải…',
  'kanban.unread': 'Chưa đọc',
} as const;

export type TranslationKey = keyof typeof vi;

export const en: Record<TranslationKey, string> = {
  // ── common ──
  'common.appName': 'Workspace Hub',
  'common.loading': 'Loading…',
  'common.save': 'Save',
  'common.saving': 'Saving…',
  'common.cancel': 'Cancel',
  'common.close': 'Close',
  'common.retry': 'Retry',
  'common.or': 'or',
  'common.comingSoon': 'Coming soon',

  // ── nav / sidebar ──
  'nav.allItems': 'All items',
  'nav.integrations': 'Connect services',
  'nav.sendEmail': 'Send email',
  'nav.scheduledEmails': 'Scheduled emails',
  'nav.admin': 'Admin',
  'nav.profile': 'Profile',
  'nav.folders': 'Folders',
  'nav.newFolder': 'New folder',
  'nav.noFolders': 'No folders yet. Create one to group items by project / client.',
  'nav.logout': 'Log out',
  'nav.loggedOut': 'Logged out',
  'nav.user': 'User',

  // ── header ──
  'header.search': 'Search across tools…',
  'header.connect': 'Connect services',
  'header.notifications': 'Notifications',

  // ── theme / language ──
  'theme.toLight': 'Switch to Light theme',
  'theme.toDark': 'Switch to Dark theme',
  'theme.light': 'Light',
  'theme.dark': 'Dark',
  'lang.label': 'Language',
  'lang.switchTo': 'Vietnamese',

  // ── login ──
  'login.title': 'Sign in',
  'login.subtitle': 'Welcome back to your Workspace Hub.',
  'login.email': 'Email',
  'login.password': 'Password',
  'login.passwordPlaceholder': 'At least 8 characters',
  'login.submit': 'Sign in',
  'login.submitting': 'Signing in…',
  'login.success': 'Signed in successfully!',
  'login.failed': 'Sign in failed. Please check your credentials.',
  'login.noAccount': "Don't have an account?",
  'login.registerLink': 'Sign up',
  'google.suffix': 'with Google',
  'google.redirecting': 'Redirecting…',

  // ── register ──
  'register.title': 'Create account',
  'register.subtitle': 'Start bringing all your work into one place.',
  'register.fullName': 'Full name',
  'register.fullNamePlaceholder': 'John Doe',
  'register.phone': 'Phone number',
  'register.password': 'Password',
  'register.confirm': 'Confirm password',
  'register.confirmPlaceholder': 'Re-enter your password',
  'register.submit': 'Sign up',
  'register.submitting': 'Creating…',
  'register.failed': 'Registration failed (the email may already be in use)',
  'register.haveAccount': 'Already have an account?',
  'register.loginLink': 'Sign in',

  // ── validation ──
  'valid.emailInvalid': 'Invalid email',
  'valid.passwordMin': 'Password must be at least 8 characters',
  'valid.nameRequired': 'Please enter your full name',
  'valid.phoneE164': 'Phone must be E.164, e.g. +84901234567',
  'valid.confirmMismatch': 'Passwords do not match',

  // ── profile ──
  'profile.title': 'My profile',
  'profile.subtitle': 'Account information and display preferences.',
  'profile.account': 'Account information',
  'profile.fullName': 'Full name',
  'profile.email': 'Email',
  'profile.role': 'Role',
  'profile.roleAdmin': 'Administrator',
  'profile.roleUser': 'User',
  'profile.avatar': 'Avatar',
  'profile.changeAvatar': 'Change avatar',
  'profile.avatarHint': 'Avatar upload will be available in an upcoming update.',
  'profile.preferences': 'Display preferences',
  'profile.appearance': 'Appearance',
  'profile.language': 'Language',

  // ── inbox / core ──
  'inbox.count': '{n} items',
  'inbox.filtering': 'Filtering:',
  'inbox.clearFilters': 'Clear filters',
  'inbox.selectAll': 'Select all on this page',
  'inbox.loadError': 'Failed to load data.',
  'inbox.emptyFiltered': 'No items match the filters.',
  'inbox.emptyFolder': 'This folder has no items yet.',
  'inbox.empty': 'No items yet.',
  'inbox.range': '{start}–{end} of {total} items',
  'integrations.title': 'Connect services',
  'integrations.subtitle': 'Grant Workspace Hub access to read and write your data.',
  'integrations.bannerTitle': 'Signing in with Google ≠ Connecting a service.',
  'integrations.bannerBody': 'Signing in only authenticates your account. For two-way sync, grant each service its own scope below.',
  'integrations.loadError': 'Could not load connections',
  'integrations.loadErrorHint': 'Please try again in a moment.',
  'integrations.connect': 'Connect',
  'integrations.reconnect': 'Reconnect',
  'integrations.disconnect': 'Disconnect',

  // ── workspace toolbar ──
  'toolbar.folder': 'Folder',
  'toolbar.folderPrefix': 'Folder · ',
  'toolbar.sync': 'Sync',
  'toolbar.syncing': 'Syncing data…',
  'toolbar.syncDone': 'Synced successfully!',
  'toolbar.list': 'List',
  'toolbar.board': 'Board',
  'toolbar.note': 'Note',
  'toolbar.event': 'Event',
  'toolbar.important': 'Important',
  'toolbar.search': 'Search title, content… (no diacritics needed)',

  // ── kanban ──
  'kanban.subtitle': 'Kanban board · drag cards to change status',
  'kanban.colInbox': 'To review',
  'kanban.colDoing': 'In progress',
  'kanban.colDone': 'Done',
  'kanban.loadError': 'Could not load the board',
  'kanban.loadErrorHint': 'Lost connection to the server. Please try again.',
  'kanban.dropHere': 'Drop cards here',
  'kanban.loadMore': 'Load more',
  'kanban.loadingMore': 'Loading…',
  'kanban.unread': 'Unread',
};

export const dictionaries = { vi, en };
export type Lang = keyof typeof dictionaries;
