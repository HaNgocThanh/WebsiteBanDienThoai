import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import App from '../App'
import { decodeSession, safeReturnPath } from '../services/auth'

// Contract fixtures only. Browser auth acceptance uses real API/SQL/mailbox.
const session = { userId: 'f438fcf3-724c-47ef-a2b1-1bb216e4f91c', email: 'test@example.invalid', fullName: 'Test customer', phone: null, emailConfirmed: true, roles: ['Customer'] }
let currentSession: typeof session | null
let commandResult: Response
let commands: { path: string; body: unknown }[]
beforeEach(() => {
  currentSession = null; commands = []; commandResult = new Response(null, { status: 204 })
  vi.stubGlobal('fetch', vi.fn(async (path: string, options: RequestInit) => {
    if (path.endsWith('/session')) return new Response(JSON.stringify(currentSession ?? { code: 'UNAUTHENTICATED' }), { status: currentSession ? 200 : 401 })
    if (path.endsWith('/csrf')) return new Response(JSON.stringify({ token: 'test-csrf' }), { status: 200 })
    if (path === '/api/v1/me') return new Response(JSON.stringify({ ...session, tierCode: 'Bronze', eligibleSpend: 0 }))
    if (path === '/api/v1/me/addresses') return new Response('[]')
    commands.push({ path, body: JSON.parse(options.body as string) })
    if (path.endsWith('/login') && commandResult.ok) currentSession = session
    return commandResult.clone()
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals() })
function renderAt(path: string) { render(<MemoryRouter initialEntries={[path]}><App /></MemoryRouter>) }
function fill(label: string, value: string) { fireEvent.change(screen.getByLabelText(label, { exact: true }), { target: { value } }) }

test('registration validates before sending and accepts a real empty 202 shape', async () => {
  commandResult = new Response(null, { status: 202 }); renderAt('/auth/register')
  fireEvent.click(screen.getByRole('button', { name: 'Tạo tài khoản' }))
  expect(screen.getByLabelText('Email').getAttribute('aria-invalid')).toBe('true'); expect(commands).toHaveLength(0)
  fill('Email', 'test@example.invalid'); fill('Họ tên', 'Test customer'); fill('Mật khẩu', 'Synthetic!Password123'); fill('Nhập lại mật khẩu', 'mismatch')
  fireEvent.click(screen.getByRole('button', { name: 'Tạo tài khoản' })); expect(commands).toHaveLength(0)
  fill('Nhập lại mật khẩu', 'Synthetic!Password123'); fireEvent.click(screen.getByRole('button', { name: 'Tạo tài khoản' }))
  expect(await screen.findByText(/Nếu thông tin phù hợp/)).toBeTruthy()
  expect(commands).toEqual([{ path: '/api/v1/auth/register', body: { email: 'test@example.invalid', password: 'Synthetic!Password123', fullName: 'Test customer', phone: null } }])
})
test('login API failure stays on the form and never reports authenticated success', async () => {
  commandResult = new Response(JSON.stringify({ code: 'EMAIL_NOT_VERIFIED', traceId: 'test-trace' }), { status: 403 }); renderAt('/auth/login')
  fill('Email', 'test@example.invalid'); fill('Mật khẩu', 'Synthetic!Password123'); fireEvent.click(screen.getByRole('button', { name: 'Đăng nhập' }))
  expect(await screen.findByText('Vui lòng xác minh email trước khi đăng nhập.')).toBeTruthy()
  expect(screen.queryByText('Xin chào, Test customer')).toBeNull()
})
test('login refreshes session and rejects an external return URL', async () => {
  renderAt('/auth/login?returnTo=https%3A%2F%2Fevil.invalid'); fill('Email', 'test@example.invalid'); fill('Mật khẩu', 'Synthetic!Password123')
  fireEvent.click(screen.getByRole('button', { name: 'Đăng nhập' }))
  expect(await screen.findByRole('heading', { name: 'Xin chào, Test customer' })).toBeTruthy()
})
test('anonymous account guard redirects to login; Customer cannot render Admin navigation', async () => {
  renderAt('/account'); expect(await screen.findByRole('heading', { name: 'Đăng nhập', level: 1 })).toBeTruthy()
  cleanup(); currentSession = session; renderAt('/admin/orders')
  expect(await screen.findByRole('heading', { name: 'Bạn không có quyền quản trị' })).toBeTruthy()
  expect(screen.queryByRole('navigation', { name: 'Điều hướng quản trị' })).toBeNull()
})
test('missing email link has a recovery path and sends no command', () => {
  renderAt('/auth/reset-password'); expect(screen.getByRole('alert').textContent).toContain('Liên kết không hợp lệ')
  expect(screen.getByRole('link', { name: 'Yêu cầu liên kết đặt lại mới' })).toBeTruthy(); expect(commands).toHaveLength(0)
})
test('email link submits only on explicit click and expired token remains an error', async () => {
  commandResult = new Response(JSON.stringify({ code: 'INVALID_TOKEN' }), { status: 400 })
  renderAt(`/auth/verify-email#userId=${session.userId}&token=test-only-token`)
  expect(commands).toHaveLength(0); fireEvent.click(screen.getByRole('button', { name: 'Xác minh email' }))
  expect(await screen.findByText('Liên kết không hợp lệ hoặc đã hết hạn. Vui lòng yêu cầu email mới.')).toBeTruthy()
  expect(screen.queryByText('Email đã được xác minh. Bạn có thể đăng nhập.')).toBeNull()
})
test('session network failure blocks protected content and offers retry', async () => {
  vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('test network'))); renderAt('/account')
  expect(await screen.findByRole('heading', { name: 'Chưa thể kiểm tra phiên' })).toBeTruthy()
  expect(screen.getByRole('button', { name: 'Thử lại' })).toBeTruthy(); expect(screen.queryByRole('button', { name: 'Đăng xuất' })).toBeNull()
})
test('logout failure does not report a successful logout', async () => {
  currentSession = session; commandResult = new Response(JSON.stringify({ code: 'INTERNAL_ERROR' }), { status: 503 }); renderAt('/account')
  fireEvent.click(await screen.findByRole('button', { name: 'Đăng xuất' }))
  expect(await screen.findByText('Chưa thể hoàn tất yêu cầu. Vui lòng thử lại.')).toBeTruthy()
  expect(screen.getByRole('heading', { name: 'Xin chào, Test customer' })).toBeTruthy()
})
test('form command is blocked while pending, including repeated submit events', async () => {
  let finish!: (response: Response) => void
  vi.stubGlobal('fetch', vi.fn(async (path: string) => {
    if (path.endsWith('/session')) return new Response(JSON.stringify({ code: 'UNAUTHENTICATED' }), { status: 401 })
    if (path.endsWith('/csrf')) return new Response(JSON.stringify({ token: 'fixture' }))
    commands.push({ path, body: null }); return await new Promise<Response>(resolve => { finish = resolve })
  }))
  renderAt('/auth/forgot-password'); fill('Email', 'test@example.invalid')
  const form = screen.getByRole('button', { name: 'Quên mật khẩu' }).closest('form')!
  fireEvent.submit(form); fireEvent.submit(form)
  await waitFor(() => expect(commands).toHaveLength(1)); finish(new Response(null, { status: 202 }))
  expect(await screen.findByText(/Nếu thông tin phù hợp/)).toBeTruthy()
})
test('session boundary requires a verified typed DTO and return URL stays local', () => {
  expect(decodeSession(session)).toEqual(session)
  expect(() => decodeSession({ ...session, emailConfirmed: false })).toThrow()
  expect(() => decodeSession({ ...session, roles: [1] })).toThrow()
  for (const path of ['//evil.invalid', '/\\evil.invalid', '/auth/login', 'https://evil.invalid', '/account#token=secret']) expect(safeReturnPath(path)).toBe('/account')
  expect(safeReturnPath('/admin/orders?page=2')).toBe('/admin/orders?page=2')
})
test('returning to a tab revalidates an expired session and removes protected content', async () => {
  currentSession = session; renderAt('/account')
  expect(await screen.findByRole('button', { name: 'Đăng xuất' })).toBeTruthy()
  currentSession = null; fireEvent.focus(window)
  expect(await screen.findByRole('heading', { name: 'Đăng nhập', level: 1 })).toBeTruthy()
  expect(screen.queryByText('Xin chào, Test customer')).toBeNull()
})
