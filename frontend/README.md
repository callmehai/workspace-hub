# Workspace Hub — Frontend

Vite + React + TypeScript + Tailwind + React Router + TanStack Query + axios + react-hot-toast.

> 📖 **Setup đầy đủ (kèm backend):** xem [`../docs/SETUP.md`](../docs/SETUP.md).

## Quick start

```bash
cd frontend
npm install
npm run dev          # http://localhost:5173
```
Cần **backend chạy ở http://localhost:5118** (xem `../backend/README.md`). FE proxy `/api` → BE nên dev không lo CORS.

## Scripts

| Lệnh | Tác dụng |
|---|---|
| `npm run dev` | dev server (HMR) |
| `npm run build` | build production (`tsc` + `vite build` → `dist/`) |
| `npm run preview` | xem thử bản build |
| `npm run lint` | ESLint |

## Cấu trúc

```
src/
  pages/        # *Page.tsx — Login, Register, Inbox
  components/   # AppLayout (shell), ProtectedRoute (token guard)
  lib/api.ts    # axios instance + JWT interceptor + 401 → /login
  App.tsx       # Router + QueryClient + Toaster
  main.tsx
vite.config.ts  # proxy /api → backend
tailwind.config.js
```

## Quy ước

- Page đặt trong `src/pages/`, hậu tố `Page`. Component tái dùng trong `src/components/`.
- Gọi API qua `api` từ `lib/api.ts` (đã gắn JWT + xử lý 401). **Không** `fetch` thủ công.
- Server state dùng **TanStack Query** (`useQuery`/`useMutation`). Client state dùng `useState`.
- Style bằng **Tailwind** utility classes. Toast qua `react-hot-toast`.

## Config

- Dev: không cần `.env` (proxy lo). 
- Prod: set `VITE_API_URL` trỏ backend đã deploy (xem `.env.example`).
