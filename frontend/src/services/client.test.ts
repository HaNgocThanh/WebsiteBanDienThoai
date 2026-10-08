import { afterEach, expect, test, vi } from 'vitest'
import { request, decodeEmpty } from './client'
import { getApiHealth } from './api'
import { ApiError, fieldError } from './errors'
import { fieldProblemFixture } from '../test/fixtures'

afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers() })
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })

test('mutation fetches cookie-bound CSRF first and sends authoritative token plus strong ETag', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(json({ token: 'test-csrf' })).mockResolvedValueOnce(new Response(null, { status: 204 }))
  vi.stubGlobal('fetch', fetchMock)
  await request('/api/v1/me', decodeEmpty, { method: 'PATCH', body: { fullName: 'Fixture' }, version: 'AAAAAAAAAAE=', headers: { 'x-csrf-token': 'untrusted', 'if-match': '"stale"' } })
  expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/auth/csrf')
  const [path, init] = fetchMock.mock.calls[1] as [string, RequestInit]
  expect(path).toBe('/api/v1/me')
  expect(new Headers(init.headers).get('X-CSRF-TOKEN')).toBe('test-csrf')
  expect(new Headers(init.headers).get('If-Match')).toBe('"AAAAAAAAAAE="')
  expect(init.credentials).toBe('same-origin')
  expect(init.redirect).toBe('error')
  expect(init.body).toBe('{"fullName":"Fixture"}')
})

test.each([json({ code: 'NOT_FOUND' }, 404), json({ token: '' }), json({ requestToken: 'wrong-contract' })])('CSRF failure never sends the mutation', async response => {
  const fetchMock = vi.fn().mockResolvedValue(response)
  vi.stubGlobal('fetch', fetchMock)
  await expect(request('/api/v1/orders', decodeEmpty, { method: 'POST', body: {} })).rejects.toBeInstanceOf(ApiError)
  expect(fetchMock).toHaveBeenCalledTimes(1)
})

test('CSRF is obtained anew after each session change instead of reusing an old token', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(json({ token: 'first-session' })).mockResolvedValueOnce(new Response(null, { status: 204 })).mockResolvedValueOnce(json({ token: 'second-session' })).mockResolvedValueOnce(new Response(null, { status: 204 }))
  vi.stubGlobal('fetch', fetchMock)
  await request('/api/v1/auth/logout', decodeEmpty, { method: 'POST' })
  await request('/api/v1/auth/login', decodeEmpty, { method: 'POST' })
  expect(new Headers(fetchMock.mock.calls[3][1].headers).get('X-CSRF-TOKEN')).toBe('second-session')
})

test('validation error preserves fields and trace with safe UI message', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(fieldProblemFixture, 400)))
  try { await getApiHealth(); throw new Error('Expected error') }
  catch (error) {
    expect(error).toBeInstanceOf(ApiError)
    const failure = error as ApiError
    expect(failure.status).toBe(400)
    expect(failure.code).toBe('VALIDATION_ERROR')
    expect(failure.traceId).toBe('test-trace')
    expect(fieldError(failure.errors, 'email')).toBe('Email không hợp lệ.')
  }
})

test.each([401, 403, 409, 412, 428, 429])('status %s is preserved and failed commands are never retried', async status => {
  const fetchMock = vi.fn().mockResolvedValueOnce(json({ token: 'test-token' })).mockResolvedValueOnce(json({ title: 'private diagnostics', code: `TEST_${status}`, traceId: 'test' }, status))
  vi.stubGlobal('fetch', fetchMock)
  await expect(request('/api/v1/orders', decodeEmpty, { method: 'POST' })).rejects.toMatchObject({ status, code: `TEST_${status}` })
  expect(fetchMock).toHaveBeenCalledTimes(2)
})

test('non-JSON error response is normalized and HTML never appears in UI', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('<html>private proxy info</html>', { status: 502 })))
  await expect(getApiHealth()).rejects.toMatchObject({ status: 502, code: 'HTTP_ERROR' })
})

test.each([{ status: 'ok' }, { status: 'ok', message: 12 }, null])('invalid success DTO is rejected', async value => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(value)))
  await expect(getApiHealth()).rejects.toMatchObject({ code: 'INVALID_RESPONSE' })
})

test('external/escaped paths and invalid RV are rejected before any fetch', async () => {
  const fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
  for (const path of ['https://example.invalid/api/v1/orders', '//example.invalid/api/v1/orders', '/api/v1/../../other', '/api/v1/me#token']) {
    await expect(request(path, decodeEmpty)).rejects.toBeInstanceOf(TypeError)
  }
  await expect(request('/api/v1/me', decodeEmpty, { version: 'AA==' })).rejects.toBeInstanceOf(TypeError)
  expect(fetchMock).not.toHaveBeenCalled()
})

test('network failure is normalized without throwing raw transport details', async () => {
  vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('private transport details')))
  await expect(getApiHealth()).rejects.toMatchObject({ code: 'NETWORK_ERROR', message: 'Không thể kết nối. Vui lòng thử lại.' })
})

test('caller abort is distinct from failure and releases pending request', async () => {
  vi.stubGlobal('fetch', vi.fn((_path: string, init: RequestInit) => new Promise((_resolve, reject) => {
    init.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')), { once: true })
  })))
  const controller = new AbortController()
  const pending = getApiHealth(controller.signal)
  const assertion = expect(pending).rejects.toMatchObject({ name: 'AbortError' })
  controller.abort()
  await assertion
})

test('timeout aborts request and exposes retryable timeout message', async () => {
  vi.useFakeTimers()
  vi.stubGlobal('fetch', vi.fn((_path: string, init: RequestInit) => new Promise((_resolve, reject) => {
    init.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')), { once: true })
  })))
  const pending = request('/api/v1/me', decodeEmpty, { timeoutMs: 50 })
  const assertion = expect(pending).rejects.toMatchObject({ code: 'TIMEOUT' })
  await vi.advanceTimersByTimeAsync(50)
  await assertion
})

test('already cancelled requests never fetch and a 204 is invalid for a required JSON DTO', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
  vi.stubGlobal('fetch', fetchMock)
  const controller = new AbortController()
  controller.abort()
  await expect(getApiHealth(controller.signal)).rejects.toMatchObject({ name: 'AbortError' })
  expect(fetchMock).not.toHaveBeenCalled()
  await expect(getApiHealth()).rejects.toMatchObject({ code: 'INVALID_RESPONSE' })
})


test('CSRF rejection keeps a distinct session-change message and never retries the mutation', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(json({ token: 'test-token' })).mockResolvedValueOnce(json({ code: 'CSRF_INVALID' }, 403))
  vi.stubGlobal('fetch', fetchMock)
  await expect(request('/api/v1/me', decodeEmpty, { method: 'PATCH' })).rejects.toMatchObject({ code: 'CSRF_INVALID', message: 'Phiên làm việc đã thay đổi. Vui lòng tải lại trang và thử lại.' })
  expect(fetchMock).toHaveBeenCalledTimes(2)
})
