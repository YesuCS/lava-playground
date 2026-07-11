// Typed clients for the two backend services.

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

export async function renderTemplate(template: string): Promise<RenderResponse> {
  const response = await fetch('/api/render', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ template }),
  })
  if (!response.ok) throw new Error(`Render API returned ${response.status}`)
  return response.json()
}

export async function fetchFilters(): Promise<FilterInfo[]> {
  const response = await fetch('/api/filters')
  if (!response.ok) throw new Error(`Filters API returned ${response.status}`)
  return response.json()
}

export async function fetchSampleContext(): Promise<unknown> {
  const response = await fetch('/api/sample-context')
  if (!response.ok) throw new Error(`Context API returned ${response.status}`)
  return response.json()
}

export async function lintTemplate(template: string): Promise<LintResponse> {
  const response = await fetch('/lint', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ template }),
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
