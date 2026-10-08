import { test, expect } from '@playwright/test'
import path from 'node:path'

// Smoke suite runs without SQL. Anonymous-session fixture is test-only;
// auth.spec.ts uses the real API/SQL/mailbox for session/guard acceptance.
test.beforeEach(async ({ page }) => {
  await page.route('**/api/v1/auth/session', route => route.fulfill({ status: 401, contentType: 'application/problem+json', body: JSON.stringify({ code: 'UNAUTHENTICATED' }) }))
})

test('browser calls real API through Vite proxy', async ({ page }) => {
  await page.goto('/health')
  const response = page.waitForResponse(r => r.url().endsWith('/api/health'))
  await page.getByRole('button', { name: 'Kiểm tra kết nối', exact: true }).click()
  expect((await response).status()).toBe(200)
  await expect(page.getByText('PhoneStore API đang hoạt động', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Kiểm tra kết nối', exact: true })).toBeEnabled()
})

test('keyboard navigation has a working skip link, route focus and active navigation', async ({ page }) => {
  await page.goto('/')
  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: 'Đến nội dung chính' })).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('main')).toBeFocused()
  const phones = page.getByRole('navigation', { name: 'Điều hướng cửa hàng' }).getByRole('link', { name: 'Điện thoại', exact: true })
  await phones.focus()
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(/\/products$/)
  await expect(phones).toHaveAttribute('aria-current', 'page')
  await expect(page.getByRole('main')).toBeFocused()
  await expect(page).toHaveTitle('Điện thoại | PhoneStore')
})

test('direct protected routes redirect after refresh and unknown store route has recovery', async ({ page }) => {
  await page.goto('/admin/orders')
  await page.reload()
  await expect(page.getByRole('heading', { level: 1, name: 'Đăng nhập' })).toBeVisible()
  await expect(page).toHaveURL(/returnTo=%2Fadmin%2Forders/)
  await expect(page.getByRole('navigation', { name: 'Điều hướng quản trị' })).toHaveCount(0)
  for (const url of ['/not-a-page']) {
    await page.goto(url)
    await expect(page.getByRole('heading', { name: 'Không tìm thấy trang', level: 1 })).toBeVisible()
    await page.getByRole('link', { name: url.startsWith('/admin') ? 'Về tổng quan' : 'Về trang chủ', exact: true }).click()
    await expect(page).toHaveURL(url.startsWith('/admin') ? /\/admin$/ : /\/$/)
  }
})

for (const width of [360, 768, 1440]) {
  test(`layouts work at ${width}px without horizontal overflow`, async ({ page }) => {
    await page.setViewportSize({ width, height: 960 })
    for (const url of ['/', '/auth/login', '/health']) {
      await page.goto(url)
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
      if (url !== '/health' && width <= 800) {
        const admin = false
        await page.getByRole('button', { name: admin ? 'Mở menu quản trị' : 'Mở menu', exact: true }).click()
        await expect(page.getByRole('navigation', { name: admin ? 'Điều hướng quản trị' : 'Điều hướng cửa hàng' })).toBeVisible()
        await page.keyboard.press('Escape')
        await expect(page.getByRole('button', { name: admin ? 'Mở menu quản trị' : 'Mở menu', exact: true })).toBeFocused()
        await expect(page.getByRole('navigation', { name: admin ? 'Điều hướng quản trị' : 'Điều hướng cửa hàng' })).toBeHidden()
      }
      if (url === '/' || (url === '/admin' && width === 1440)) {
        await page.screenshot({ path: path.resolve('../docs/agent-kit/tasks', `P1-01-${url === '/' ? 'store' : 'admin'}-${width}.png`), fullPage: true })
      }
    }
  })
}

test('search validates, preserves query and remains safe text in the URL and page', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('button', { name: 'Tìm kiếm' }).click()
  await expect(page.getByRole('searchbox')).toHaveAttribute('aria-invalid', 'true')
  await expect(page.getByRole('alert')).toContainText('Nhập tên điện thoại')
  await page.getByRole('searchbox').fill('phone & 128GB')
  await page.getByRole('button', { name: 'Tìm kiếm' }).click()
  await expect(page).toHaveURL(/\/products\?search=phone%20%26%20128GB$/)
  await expect(page.getByText('phone & 128GB', { exact: true })).toBeVisible()
  await page.reload()
  await expect(page.getByText('phone & 128GB', { exact: true })).toBeVisible()
})

test('HTTP error remains an error and retry can recover without stale success', async ({ page }) => {
  // Error injection only in this test; success uses the real API.
  await page.goto('/health')
  await page.route('**/api/health', route => route.fulfill({ status: 429, contentType: 'application/problem+json', body: JSON.stringify({ code: 'RATE_LIMITED', traceId: 'browser-test' }) }))
  await page.getByRole('button', { name: 'Kiểm tra kết nối', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('Bạn thao tác quá nhanh')
  await expect(page.getByText('PhoneStore API đang hoạt động', { exact: true })).toHaveCount(0)
  await page.unroute('**/api/health')
  await page.getByRole('button', { name: 'Kiểm tra kết nối', exact: true }).click()
  await expect(page.getByText('PhoneStore API đang hoạt động', { exact: true })).toBeVisible()
  await expect(page.getByRole('alert')).toHaveCount(0)
})
