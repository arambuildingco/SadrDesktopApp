/**
 * REST client for the Sadr API.
 * The token pair lives in localStorage; every call refreshes the access token once
 * when the API answers 401, so a long-lived desktop session keeps working.
 */

export type Json = Record<string, any>
export type Paged<T = Json> = { items: T[]; total: number; page: number; pageSize: number }

const ACCESS = 'sadr.access'
const REFRESH = 'sadr.refresh'
const USER = 'sadr.user'

export type SessionUser = { id: string; username: string; email: string; role: string }

export const auth = {
  get token() {
    return localStorage.getItem(ACCESS) ?? ''
  },
  get refreshToken() {
    return localStorage.getItem(REFRESH) ?? ''
  },
  get user(): SessionUser | null {
    const raw = localStorage.getItem(USER)
    if (!raw) return null
    try {
      return JSON.parse(raw) as SessionUser
    } catch {
      return null
    }
  },
  save(payload: { accessToken: string; refreshToken: string; user: SessionUser }) {
    localStorage.setItem(ACCESS, payload.accessToken)
    localStorage.setItem(REFRESH, payload.refreshToken)
    localStorage.setItem(USER, JSON.stringify(payload.user))
  },
  clear() {
    localStorage.removeItem(ACCESS)
    localStorage.removeItem(REFRESH)
    localStorage.removeItem(USER)
  }
}

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function parseError(response: Response): Promise<string> {
  try {
    const body = await response.json()
    if (body?.message) return body.message
    if (body?.title) return body.title
    if (body?.errors) return Object.values(body.errors).flat().join(' ')
    return `خطای ${response.status}`
  } catch {
    return `خطای ${response.status}`
  }
}

let refreshing: Promise<boolean> | null = null

async function tryRefresh(): Promise<boolean> {
  if (!auth.refreshToken) return false
  if (!refreshing) {
    refreshing = (async () => {
      const response = await fetch('/api/auth/refresh', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken: auth.refreshToken })
      })
      if (!response.ok) {
        auth.clear()
        return false
      }
      const data = await response.json()
      auth.save(data)
      return true
    })().finally(() => {
      refreshing = null
    })
  }
  return refreshing
}

async function request<T>(method: string, url: string, body?: unknown, retry = true): Promise<T> {
  const headers: Record<string, string> = {}
  if (auth.token) headers.Authorization = `Bearer ${auth.token}`
  let payload: BodyInit | undefined
  if (body instanceof FormData) {
    payload = body
  } else if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
    payload = JSON.stringify(body)
  }

  const response = await fetch(url, { method, headers, body: payload })

  if (response.status === 401 && retry && (await tryRefresh())) {
    return request<T>(method, url, body, false)
  }
  if (!response.ok) throw new ApiError(response.status, await parseError(response))
  if (response.status === 204) return undefined as T

  const contentType = response.headers.get('content-type') ?? ''
  return (contentType.includes('application/json') ? await response.json() : (await response.text())) as T
}

export const api = {
  get: <T = Json>(url: string) => request<T>('GET', url),
  post: <T = Json>(url: string, body?: unknown) => request<T>('POST', url, body),
  put: <T = Json>(url: string, body?: unknown) => request<T>('PUT', url, body),
  del: <T = void>(url: string) => request<T>('DELETE', url),
  upload: <T = Json>(url: string, form: FormData) => request<T>('POST', url, form),
  /** Every CRUD collection answers { items, total, ... }; this unwraps the items. */
  list: async <T = Json>(url: string): Promise<T[]> => {
    const data = await request<Paged<T> | T[]>('GET', url)
    return Array.isArray(data) ? data : data.items
  }
}

/** Query-string builder that skips empty values. */
export function qs(params: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams()
  Object.entries(params).forEach(([key, value]) => {
    if (value === undefined || value === null || value === '') return
    search.set(key, String(value))
  })
  const text = search.toString()
  return text ? `?${text}` : ''
}

export const money = (value: number | null | undefined) =>
  (value ?? 0).toLocaleString('fa-IR', { maximumFractionDigits: 0 })

export const number = (value: number | null | undefined, digits = 0) =>
  (value ?? 0).toLocaleString('fa-IR', { maximumFractionDigits: digits })
