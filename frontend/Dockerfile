# Build SPA rồi phục vụ tĩnh bằng Caddy (kèm auto-HTTPS Let's Encrypt).
# FE mặc định gọi API qua '/api' (xem src/lib/api.ts) ⇒ Caddy reverse-proxy same-origin,
# không cần VITE_API_URL, không dính CORS.
# node:22-slim (Debian/glibc) thay vì alpine/musl — rolldown (Vite 8) có sẵn binary native glibc.
# Dùng `npm install` (KHÔNG npm ci): lockfile sinh trên macOS thiếu optional deps chỉ-Linux
# (@emnapi/*), npm ci strict sẽ fail; npm install resolve đúng theo nền tảng lúc cài.
FROM node:22-slim AS build
WORKDIR /app
COPY package*.json ./
RUN npm install --no-audit --no-fund
COPY . .
RUN npm run build

FROM caddy:2-alpine
COPY --from=build /app/dist /srv
# Caddyfile được mount qua docker-compose (deploy/Caddyfile). Cổng 80/443 do compose map.
