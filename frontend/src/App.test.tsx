import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import App from './App'
import { TextField, ErrorNotice } from './components/primitives'
import { ApiError } from './services/errors'
import { fieldProblemFixture } from './test/fixtures'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })
const adminSessionFixture = { userId: 'f438fcf3-724c-47ef-a2b1-1bb216e4f91c', email: 'admin@example.invalid', fullName: 'Test admin', phone: null, emailConfirmed: true, roles: ['Admin'] }
beforeEach(() => vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({ code: 'UNAUTHENTICATED' }), { status: 401 })))))
function mockHealth(fetchMock: typeof fetch) { vi.stubGlobal('fetch', (input: RequestInfo | URL, init?: RequestInit) => String(input).endsWith('/auth/session') ? Promise.resolve(new Response(JSON.stringify({ code: 'UNAUTHENTICATED' }), { status: 401 })) : fetchMock(input, init)) }
function renderAt(path: string) { return render(<MemoryRouter initialEntries={[path]}><App /></MemoryRouter>) }

test('health success displays validated API data and prevents duplicate click while pending', async () => {
  let complete!: (value: Response) => void
  const fetchMock = vi.fn(() => new Promise<Response>(resolve => { complete = resolve }))
  mockHealth(fetchMock)
  renderAt('/health')
  fireEvent.click(screen.getByRole('button', { name: 'Kiểm tra kết nối' }))
  expect((screen.getByRole('button', { name: 'Đang kiểm tra…' }) as HTMLButtonElement).disabled).toBe(true)
  complete(new Response(JSON.stringify({ status: 'ok', message: 'Health test fixture' }), { status: 200 }))
  expect(await screen.findByText('Health test fixture')).toBeTruthy()
  expect(fetchMock).toHaveBeenCalledTimes(1)
  expect(fetchMock).toHaveBeenCalledWith('/api/health', expect.objectContaining({ credentials: 'same-origin' }))
  expect((screen.getByRole('button', { name: 'Kiểm tra kết nối' }) as HTMLButtonElement).disabled).toBe(false)
})

test('HTTP failure displays safe error and allows retry without reporting success', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'NOT_READY', traceId: 'test-trace' }), { status: 503 })))
  renderAt('/health')
  fireEvent.click(screen.getByRole('button', { name: 'Kiểm tra kết nối' }))
  expect(await screen.findByText('Chưa thể hoàn tất yêu cầu. Vui lòng thử lại.')).toBeTruthy()
  expect(screen.queryByText('PhoneStore API đang hoạt động')).toBeNull()
  expect((screen.getByRole('button', { name: 'Kiểm tra kết nối' }) as HTMLButtonElement).disabled).toBe(false)
})

test('store navigation changes page, title and focus', () => {
  renderAt('/')
  fireEvent.click(within(screen.getByRole('navigation', { name: 'Điều hướng cửa hàng' })).getByRole('link', { name: 'Điện thoại' }))
  expect(screen.getByRole('heading', { level: 1, name: 'Điện thoại' })).toBeTruthy()
  expect(document.activeElement).toBe(screen.getByRole('main'))
  expect(document.title).toBe('Điện thoại | PhoneStore')
})

test('search field exposes validation and encodes text safely into navigation', () => {
  renderAt('/')
  fireEvent.click(screen.getByRole('button', { name: 'Tìm kiếm' }))
  const input = screen.getByRole('searchbox', { name: 'Tìm điện thoại' })
  expect(input.getAttribute('aria-invalid')).toBe('true')
  expect(screen.getByRole('alert').textContent).toContain('Nhập tên điện thoại')
  fireEvent.change(input, { target: { value: '<img src=x onerror=alert(1)> & phone' } })
  fireEvent.click(screen.getByRole('button', { name: 'Tìm kiếm' }))
  expect(screen.getByText('<img src=x onerror=alert(1)> & phone')).toBeTruthy()
  expect(screen.queryByRole('img')).toBeNull()
})

test('admin shell is distinct and links to available catalog tools without fake statistics', async () => {
  vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify(adminSessionFixture)))))
  renderAt('/admin')
  expect(await screen.findByRole('navigation', { name: 'Điều hướng quản trị' })).toBeTruthy()
  expect(screen.queryByRole('searchbox')).toBeNull()
  expect(screen.getByText('Quản lý danh mục, tồn kho, đơn hàng và thông báo của cửa hàng.')).toBeTruthy()
  fireEvent.click(within(screen.getByRole('navigation', { name: 'Điều hướng quản trị' })).getByRole('link', { name: 'Danh mục sản phẩm' }))
  expect(screen.getByRole('heading', { level: 1, name: 'Danh mục sản phẩm' })).toBeTruthy()
})

test.each(['/missing', '/admin/missing'])('unknown route %s provides a recoverable 404', async path => {
  if (path.startsWith('/admin')) vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify(adminSessionFixture)))))
  renderAt(path)
  expect(await screen.findByRole('heading', { level: 1, name: 'Không tìm thấy trang' })).toBeTruthy()
  expect(screen.getByRole('link', { name: path.startsWith('/admin') ? 'Về tổng quan' : 'Về trang chủ' })).toBeTruthy()
})

test('form primitive links label, hint and field error without using placeholder as label', () => {
  render(<TextField label="Email" id="email" hint="Email của bạn" error="Email không hợp lệ" />)
  const input = screen.getByLabelText('Email')
  expect(input.getAttribute('aria-describedby')).toBe('email-hint email-error')
  expect(input.getAttribute('aria-invalid')).toBe('true')
  expect(screen.getByRole('alert').id).toBe('email-error')
})

test('ProblemDetails field errors remain visible and accessible', () => {
  render(<ErrorNotice error={new ApiError(400, fieldProblemFixture.code, fieldProblemFixture.traceId, fieldProblemFixture.errors)} />)
  expect(screen.getByRole('alert').textContent).toContain('Email không hợp lệ.')
})
