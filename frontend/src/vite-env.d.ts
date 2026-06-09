/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Base URL của backend API. Bỏ trống ở dev (dùng proxy '/api' trong vite.config.ts). */
  readonly VITE_API_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
