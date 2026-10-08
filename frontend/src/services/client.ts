import type { RowVersion } from '../types/contracts'
import { ApiError, isRecord, parseProblem } from './errors'

export type Decoder<T> = (value: unknown) => T
export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'
  body?: unknown
  headers?: Record<string, string>
  version?: RowVersion
  signal?: AbortSignal
  timeoutMs?: number
}

function apiPath(path: string): string {
  if (!path.startsWith('/') || path.startsWith('//')) throw new TypeError('API path must be relative to this origin.')
  const url = new URL(path, 'https://request.invalid')
  if (url.origin !== 'https://request.invalid' || url.hash
    || !(url.pathname.startsWith('/api/v1/') || ['/api/health', '/api/ready'].includes(url.pathname))) {
    throw new TypeError('Unsupported API path.')
  }
  return url.pathname + url.search
}

function etag(version: RowVersion): string {
  try {
    const bytes = atob(version)
    if (bytes.length === 8 && btoa(bytes) === version) return `"${version}"`
  } catch { /* Invalid input is rejected before fetch. */ }
  throw new TypeError('Version must be canonical base64 for an eight-byte rowversion.')
}

async function execute<T>(path: string, decode: Decoder<T>, options: RequestOptions = {}): Promise<T> {
  if (options.signal?.aborted) throw new DOMException('Request aborted', 'AbortError')
  const timeoutMs = options.timeoutMs ?? 15000
  if (!Number.isFinite(timeoutMs) || timeoutMs <= 0) throw new TypeError('Timeout must be positive.')
  const controller = new AbortController()
  const abort = () => controller.abort(options.signal?.reason)
  if (options.signal?.aborted) abort()
  else options.signal?.addEventListener('abort', abort, { once: true })
  let timedOut = false
  const timer = setTimeout(() => { timedOut = true; controller.abort() }, timeoutMs)
  try {
    const headers = new Headers(options.headers)
    headers.set('Accept', 'application/json')
    if (options.body !== undefined) headers.set('Content-Type', 'application/json')
    const response = await fetch(path, {
      method: options.method ?? 'GET', credentials: 'same-origin', cache: 'no-store', redirect: 'error',
      headers, signal: controller.signal,
      ...(options.body === undefined ? {} : { body: JSON.stringify(options.body) }),
    })
    let payload: unknown
    try { const body = await response.text(); payload = body.trim() ? JSON.parse(body) : undefined }
    catch {
      if (controller.signal.aborted) throw new DOMException('Request aborted', 'AbortError')
      if (response.ok) throw new ApiError(response.status, 'INVALID_RESPONSE')
    }
    if (!response.ok) {
      const problem = parseProblem(payload, response.status)
      throw new ApiError(response.status, problem.code, problem.traceId, problem.errors)
    }
    try { return decode(payload) }
    catch { throw new ApiError(response.status, 'INVALID_RESPONSE') }
  } catch (error) {
    if (error instanceof ApiError && error.status === 401 && path !== '/api/v1/auth/session' && path !== '/api/v1/auth/login') window.dispatchEvent(new Event('auth-session-invalid'))
    if (options.signal?.aborted) throw new DOMException('Request aborted', 'AbortError')
    if (timedOut) throw new ApiError(0, 'TIMEOUT')
    if (error instanceof ApiError) throw error
    throw new ApiError(0, 'NETWORK_ERROR')
  } finally {
    clearTimeout(timer)
    options.signal?.removeEventListener('abort', abort)
  }
}

export async function request<T>(path: string, decode: Decoder<T>, options: RequestOptions = {}): Promise<T> {
  const target = apiPath(path)
  const method = options.method ?? 'GET'
  if (method === 'GET' && options.body !== undefined) throw new TypeError('GET requests cannot contain a body.')
  const headers = new Headers(options.headers)
  if (options.version !== undefined) headers.set('If-Match', etag(options.version))
  if (method !== 'GET') {
    if (!target.startsWith('/api/v1/')) throw new TypeError('Mutation requires a versioned API path.')
    const token = await execute('/api/v1/auth/csrf', value => {
      if (!isRecord(value) || typeof value.token !== 'string' || value.token.trim().length === 0) {
        throw new TypeError('Invalid CSRF response.')
      }
      return value.token
    }, { signal: options.signal, timeoutMs: options.timeoutMs })
    // Always overwrite a caller-provided token with the token for the current cookie session.
    headers.set('X-CSRF-TOKEN', token)
    return execute(target, decode, { ...options, headers: Object.fromEntries(headers) })
  }
  return execute(target, decode, { ...options, headers: Object.fromEntries(headers) })
}

export const decodeEmpty: Decoder<void> = value => {
  if (value !== undefined) throw new TypeError('Expected an empty response.')
}
