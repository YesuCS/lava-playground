/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Base URL of the C# render API. Empty in dev (Vite proxies /api). */
  readonly VITE_RENDER_BASE?: string
  /** Base URL of the Python lint API. Empty in dev (Vite proxies /lint). */
  readonly VITE_LINT_BASE?: string
  /** "owner/repo" the desktop app checks for newer versions. Empty on web. */
  readonly VITE_UPDATE_REPO?: string
  /** Git commit the desktop app was built from, for the update check. */
  readonly VITE_BUILD_COMMIT?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}

declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<object, object, unknown>
  export default component
}
