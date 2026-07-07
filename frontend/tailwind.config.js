/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  // Dark mode bật theo class `.dark` trên <html> (ThemeContext toggle) — KHÔNG theo prefers-color-scheme,
  // để user chủ động chọn và đổi tức thì (chỉ toggle class, không remount component nào).
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        // `brand` = indigo (SSOT màu thương hiệu). Trước đây brand là blue-600 (#2563eb) trong khi
        // sidebar/inbox dùng indigo-600 (#4f46e5) → logo/nút Login/Header lệch tông so với app.
        // Gộp brand = indigo để mọi bề mặt (login, header, sidebar, nút, focus ring) đồng nhất 1 tông.
        brand: {
          50: '#eef2ff',
          100: '#e0e7ff',
          200: '#c7d2fe',
          300: '#a5b4fc',
          400: '#818cf8',
          500: '#6366f1',
          600: '#4f46e5',
          700: '#4338ca',
          800: '#3730a3',
          900: '#312e81',
        },
      },
      fontFamily: {
        // Fallback stack ĐẦY ĐỦ theo system font — nếu Inter chưa kịp load / fail,
        // chữ vẫn render bằng font hệ thống metric gần tương đương (không lệch chiều cao ký tự).
        sans: [
          'InterVariable',
          'Inter',
          '-apple-system',
          'BlinkMacSystemFont',
          '"Segoe UI"',
          'Roboto',
          '"Helvetica Neue"',
          'Arial',
          '"Noto Sans"',
          'sans-serif',
        ],
      },
    },
  },
  plugins: [],
}
