import { expect, test } from '@playwright/test'
import { setupAdmin } from './adminHelpers.js'
test('Admin customer search edit retry and history use real API; tier editing is removed', async ({ browser }) => {
  const state = await setupAdmin(browser), context = await browser.newContext({ storageState: state }), page = await context.newPage()
  await page.goto('/admin/customers'); await expect(page.getByRole('heading', { name: 'Quản lý khách hàng', exact: true })).toBeVisible(); await expect(page.getByRole('link', { name: 'Hạng khách hàng', exact: true })).toHaveCount(0)
  const session = await (await page.request.get('/api/v1/auth/session')).json() as { userId: string; email: string }
  await page.getByLabel('Tìm tên hoặc email khách hàng').fill(session.email); await page.getByRole('button', { name: 'Tìm khách hàng', exact: true }).click(); await expect(page.getByRole('link', { name: 'Catalog browser admin', exact: true })).toHaveCount(1); await page.getByRole('link', { name: 'Catalog browser admin', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Chi tiết khách hàng', exact: true })).toBeVisible(); await expect(page.getByLabel('Email khách hàng')).toHaveValue(session.email); await expect(page.getByLabel('Tài khoản hoạt động')).toBeDisabled()
  await page.getByLabel('Họ tên khách hàng').fill('Supported browser customer'); await page.getByLabel('Số điện thoại khách hàng').fill('0900000000')
  let sent: unknown, lost = false
  await page.route('**/api/v1/admin/customers/*/profile', async route => {
    if (route.request().method() !== 'PUT') { await route.continue(); return }
    if (!lost) { sent = route.request().postDataJSON() as unknown; expect((await route.fetch()).status()).toBe(200); lost = true; await route.abort('failed') }
    else { expect(route.request().postDataJSON()).toEqual(sent); await route.continue() }
  })
  await page.getByRole('button', { name: 'Lưu thông tin khách hàng', exact: true }).click(); await expect(page.getByRole('alert')).toBeVisible(); await expect(page.getByLabel('Họ tên khách hàng')).toBeDisabled(); await page.getByRole('button', { name: 'Thử cập nhật lại', exact: true }).click(); await expect(page.getByText('Đã cập nhật thông tin khách hàng.')).toBeVisible()
  await page.reload(); await expect(page.getByLabel('Họ tên khách hàng')).toHaveValue('Supported browser customer'); await expect(page.getByLabel('Số điện thoại khách hàng')).toHaveValue('0900000000')
  for (const width of [360, 768, 1440]) { await page.setViewportSize({ width, height: 900 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true) }
  await page.getByRole('link', { name: 'Xem đơn hàng của khách' }).click(); await expect(page.getByLabel('ID tài khoản khách')).toHaveValue(session.userId)
  const csrf = (await (await page.request.get('/api/v1/auth/csrf')).json() as { token: string }).token
  expect((await page.request.put('/api/v1/admin/customer-tiers', { headers: { 'X-CSRF-TOKEN': csrf }, data: {} })).status()).toBe(405)
  await context.close()
})
