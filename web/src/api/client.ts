/** A ProblemDetails response from the API; `detail` is already in Spanish. */
export class ApiProblem extends Error {
  readonly status: number
  readonly code: string | null
  readonly errors: Record<string, string[]> | null

  constructor(status: number, detail: string, code: string | null, errors: Record<string, string[]> | null) {
    super(detail)
    this.name = 'ApiProblem'
    this.status = status
    this.code = code
    this.errors = errors
  }
}

async function toProblem(response: Response): Promise<ApiProblem> {
  let body: { detail?: string; title?: string; code?: string; errors?: Record<string, string[]> } | null = null
  try {
    body = await response.json()
  } catch {
    // not JSON (proxy error page, API down)
  }
  const fieldErrors = body?.errors ? Object.values(body.errors).flat().join(' ') : ''
  const detail = body?.detail || fieldErrors || body?.title || `La solicitud falló (HTTP ${response.status}).`
  return new ApiProblem(response.status, detail, body?.code ?? null, body?.errors ?? null)
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const response = await fetch(path, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) throw await toProblem(response)
  if (response.status === 204) return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const http = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body ?? {}),
}

export function query(params: Record<string, string | number | undefined | null>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') search.set(key, String(value))
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}
