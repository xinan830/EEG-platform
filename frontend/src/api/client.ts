const apiBase = import.meta.env.VITE_API_BASE ?? 'http://127.0.0.1:8000'

export interface ApiErrorPayload {
  code?: string
  message?: string
  detail?: string
  request_id?: string
}

export function normalizeApiError(payload: unknown): string {
  if (typeof payload === 'object' && payload !== null) {
    const value = payload as ApiErrorPayload
    if (typeof value.message === 'string') return value.message
    if (typeof value.detail === 'string') return value.detail
  }
  return '请求失败，请检查后端服务和输入数据。'
}

export async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  if (init.body && !(init.body instanceof FormData)) headers.set('Content-Type', 'application/json')
  const response = await fetch(`${apiBase}${path}`, { ...init, headers })
  const payload: unknown = await response.json().catch(() => null)
  if (!response.ok) throw new Error(normalizeApiError(payload))
  return payload as T
}
