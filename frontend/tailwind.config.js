/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        brand: {
          50: '#eff6ff',
          100: '#dbeafe',
          500: '#5c6bc0',
          600: '#4f5b93',
          700: '#3f4870',
        },
        dark: {
          900: '#111111', // Main background
          800: '#18181b', // Sidebar / Panel background
          700: '#27272a', // Input / Hover background
          600: '#3f3f46', // Border
        }
      },
      fontFamily: {
        sans: ['Inter', 'sans-serif'],
      },
    },
  },
  plugins: [],
}
