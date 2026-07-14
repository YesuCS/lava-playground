// Typed clients for the two backend services.
//
// In `npm run dev` these are empty, so requests go to `/api` and `/lint` and
// the Vite dev server proxies them. In the packaged desktop app the two
// backends run as sidecar processes on fixed localhost ports, so the build
// sets VITE_RENDER_BASE / VITE_LINT_BASE to point straight at them (both
// services send `Access-Control-Allow-Origin: *`, so cross-origin is fine).
const RENDER_BASE = import.meta.env.VITE_RENDER_BASE ?? ''
const LINT_BASE = import.meta.env.VITE_LINT_BASE ?? ''

export interface RenderResponse {
  output: string | null
  elapsedMs: number
  error: string | null
}

export interface FilterInfo {
  name: string
  category: string
  description: string
  example: string
}

export interface LintIssue {
  line: number
  col: number
  severity: 'error' | 'warning' | 'info'
  code: string
  message: string
  suggestion: string | null
}

export interface LintResponse {
  issues: LintIssue[]
  counts: { error: number; warning: number; info: number }
}

export type Engine = 'local' | 'rock'

export interface RockStatus {
  connected: boolean
  baseUrl: string | null
  authType: string | null
}

export interface RockConnectRequest {
  baseUrl: string
  apiKey?: string
  username?: string
  password?: string
}

export async function renderTemplate(template: string, engine: Engine = 'local'): Promise<RenderResponse> {
  const path = engine === 'rock' ? '/api/rock/render' : '/api/render'
  const response = await fetch(`${RENDER_BASE}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ template }),
  })
  if (!response.ok) throw new Error(`Render API returned ${response.status}`)
  return response.json()
}

export async function rockStatus(): Promise<RockStatus> {
  const response = await fetch(`${RENDER_BASE}/api/rock/status`)
  if (!response.ok) throw new Error(`Status returned ${response.status}`)
  return response.json()
}

export async function rockConnect(request: RockConnectRequest): Promise<RockStatus> {
  const response = await fetch(`${RENDER_BASE}/api/rock/connect`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  })
  const body = await response.json()
  if (!response.ok) throw new Error(body.error ?? `Connect failed with ${response.status}`)
  return body
}

export async function rockDisconnect(): Promise<RockStatus> {
  const response = await fetch(`${RENDER_BASE}/api/rock/disconnect`, { method: 'POST' })
  return response.json()
}

export async function fetchFilters(): Promise<FilterInfo[]> {
  const response = await fetch(`${RENDER_BASE}/api/filters`)
  if (!response.ok) throw new Error(`Filters API returned ${response.status}`)
  return response.json()
}

export async function fetchSampleContext(): Promise<unknown> {
  const response = await fetch(`${RENDER_BASE}/api/sample-context`)
  if (!response.ok) throw new Error(`Context API returned ${response.status}`)
  return response.json()
}

export async function lintTemplate(template: string, engine: Engine = 'local'): Promise<LintResponse> {
  const response = await fetch(`${LINT_BASE}/lint`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ template, engine }),
  })
  if (!response.ok) throw new Error(`Lint API returned ${response.status}`)
  return response.json()
}

/** Debounce helper shared by the editor's render + lint round-trips. */
export function debounce<Args extends unknown[]>(fn: (...args: Args) => void, ms: number) {
  let timer: ReturnType<typeof setTimeout> | undefined
  return (...args: Args) => {
    clearTimeout(timer)
    timer = setTimeout(() => fn(...args), ms)
  }
}
