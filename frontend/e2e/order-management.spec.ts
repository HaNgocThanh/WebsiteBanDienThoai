import { test, expect } from '@playwright/test'
import type { BrowserContext, Page } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import { readFile, readdir } from 'node:fs/promises'
import path from 'node:path'
import { setupAdmin, createProduct, addVariant } from './adminHelpers.js'
import { selectDestination } from './addressHelpers.js'
if (!/Database=PhoneStore_Test_[0-9a-f]{32};/.test(process.env.PHONESTORE_E2E_SQL ?? '')) throw new Error('Use isolated GUID SQL harness')
let state: Awaited<ReturnType<BrowserContext['storageState']>>, product: Awaited<ReturnType<typeof createProduct>>, variantId: string
async function post(page: Page, route: string, data?: unknown) { const csrf = (await (await page.request.get('/api/v1/auth/csrf')).json() as { token: string }).token; return page.request.post('/api/v1/' + route, { data, headers: { 'X-CSRF-TOKEN': csrf } }) }
test.beforeAll(async ({ browser }) => {
  state = await setupAdmin(browser); const context = await browser.newContext({ storageState: state }), page = await context.newPage(); product = await createProduct(page); await addVariant(page, 'ORDER-READ-' + product.suffix, 'Đen')
  variantId = ((await (await page.request.get('/api/v1/admin/products/' + product.id)).json()) as { variants: { id: string }[] }).variants[0].id
  expect((await post(page, 'admin/inventory/' + variantId + '/receipts', { quantity: 10, reason: 'Synthetic order management', operationKey: randomUUID() })).status()).toBe(201); await context.close()
})
async function customer(page: Page) {
  const email = 'orders-' + randomUUID() + '@example.invalid', password = 'Synthetic!Password123'; await page.goto('/')
  expect((await post(page, 'auth/register', { email, password, fullName: 'Synthetic order reader' })).status()).toBe(202)
  let mail: { UserId: string; Token: string } | undefined
  await expect.poll(async () => { for (const file of await readdir(process.env.PHONESTORE_E2E_MAILBOX!)) { if (!file.endsWith('.json')) continue; const m = JSON.parse(await readFile(path.join(process.env.PHONESTORE_E2E_MAILBOX!, file), 'utf8')) as { Email: string; Purpose: string; UserId: string; Token: string }; if (m.Email === email && m.Purpose === 'verify-email') mail = m } return !!mail }).toBe(true)
  expect((await post(page, 'auth/verify-email', { userId: mail!.UserId, token: mail!.Token })).status()).toBe(204)
  await page.goto('/auth/login?returnTo=/account/orders'); await page.getByLabel('Email', { exact: true }).fill(email); await page.getByLabel('Mật khẩu', { exact: true }).fill(password); await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Đơn hàng của tôi' })).toBeVisible(); return email
}
async function place(page: Page) {
  await page.goto('/products/phone-' + product.suffix); await page.getByRole('radio').first().check(); await page.getByRole('button', { name: 'Thêm vào giỏ hàng', exact: true }).click(); await page.getByRole('link', { name: 'Xem giỏ hàng' }).click(); await page.getByRole('link', { name: 'Tiến hành thanh toán' }).click()
  await page.getByLabel('Họ tên người nhận', { exact: true }).fill('Snapshot order reader'); await page.getByLabel('Số điện thoại', { exact: true }).fill('0000000000'); await selectDestination(page, '79'); await page.getByRole('button', { name: 'Tính báo giá', exact: true }).click(); await page.getByLabel('Tôi xác nhận sản phẩm, số lượng và tổng tiền trên.').check()
  const response = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/api/v1/orders')); await page.getByRole('button', { name: 'Đặt hàng', exact: true }).click(); const order = await (await response).json() as { id: string; orderNumber: string }; await expect(page.getByRole('heading', { name: 'Đặt hàng thành công', exact: true })).toBeVisible(); await page.getByRole('button', { name: 'Tiếp tục mua sắm' }).click(); return order
}
test('customer order paging/detail, Admin email/day filters and private note lost-response retry use SQL', async ({ page, browser }) => {
  const email = await customer(page); const first = await place(page); await place(page)
  await page.goto('/account/orders?pageSize=1'); await expect(page.locator('.order-row')).toHaveCount(1); await page.getByRole('button', { name: 'Trang sau', exact: true }).click(); await expect(page).toHaveURL(/page=2/); await page.reload(); await expect(page.locator('.order-row')).toContainText(first.orderNumber)
  await page.locator('.order-row').getByRole('link').click(); await expect(page.getByRole('heading', { name: 'Đơn ' + first.orderNumber })).toBeVisible(); await expect(page.getByText(product.name, { exact: true })).toBeVisible(); await expect(page.getByText('Snapshot order reader · 0000000000')).toBeVisible(); await expect(page.getByRole('heading', { name: 'Ghi chú nội bộ' })).toHaveCount(0)
  const context = await browser.newContext({ storageState: state }), admin = await context.newPage(); await admin.goto('/admin/orders'); await admin.getByLabel('Email khách hàng').fill(email)
  const day = new Date(Date.now() + 7 * 3600000).toISOString().slice(0, 10); await admin.getByLabel('Từ ngày', { exact: true }).fill(day); await admin.getByLabel('Đến hết ngày').fill(day); await admin.getByLabel('Số đơn mỗi trang').selectOption('1')
  const query = admin.waitForResponse(r => r.request().method() === 'GET' && r.url().includes('/api/v1/admin/orders?') && r.url().includes('email=')); await admin.getByRole('button', { name: 'Lọc đơn hàng' }).click(); const response = await query; expect((await response.json() as { totalCount: number }).totalCount).toBe(2); expect(new URL(response.url()).searchParams.get('from')).toBe(new Date(day + 'T00:00:00+07:00').toISOString())
  await admin.goto('/admin/orders/' + first.id); await admin.getByLabel('Nội dung ghi chú nội bộ').fill('PRIVATE SYNTHETIC browser note')
  let sent: unknown; let lost = false
  await admin.route('**/api/v1/admin/orders/' + first.id + '/notes', async route => { if (!lost) { sent = route.request().postDataJSON() as unknown; expect((await route.fetch()).status()).toBe(200); lost = true; await route.abort('failed') } else { expect(route.request().postDataJSON()).toEqual(sent); await route.continue() } })
  await admin.getByRole('button', { name: 'Lưu ghi chú', exact: true }).click(); await expect(admin.getByRole('alert')).toBeVisible(); await expect(admin.getByLabel('Nội dung ghi chú nội bộ')).toBeDisabled(); await admin.getByRole('button', { name: 'Thử lưu lại ghi chú' }).click(); await expect(admin.locator('.order-note')).toHaveCount(1); await expect(admin.locator('.order-note')).toContainText('PRIVATE SYNTHETIC browser note')
  await page.reload(); await expect(page.getByText('PRIVATE SYNTHETIC browser note')).toHaveCount(0); expect((await page.request.get('/api/v1/admin/orders/' + first.id)).status()).toBe(403)
  for (const width of [360, 768, 1440]) { await page.setViewportSize({ width, height: 900 }); await admin.setViewportSize({ width, height: 900 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true); expect(await admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true); await admin.screenshot({ path: path.resolve('../docs/agent-kit/tasks', 'P4-01-admin-' + width + '.png'), fullPage: true }); await page.screenshot({ path: path.resolve('../docs/agent-kit/tasks', 'P4-01-user-' + width + '.png'), fullPage: true }) }
  await context.close()
})
test('hosted notification unread badge/read/filter survives reload and points to owned order', async ({ page }) => {
  await customer(page); const order = await place(page); await page.goto('/account/notifications')
  await expect.poll(async () => ((await (await page.request.get('/api/v1/me/notifications')).json()) as { unreadCount: number }).unreadCount, { timeout: 15000 }).toBe(1); await page.reload(); await expect(page.getByText('1 thông báo chưa đọc', { exact: true })).toBeVisible(); await expect(page.getByRole('link', { name: 'Thông báo (1)' })).toBeVisible()
  await page.getByLabel('Chỉ xem chưa đọc').check(); await page.getByRole('button', { name: 'Đánh dấu đã đọc' }).click(); await expect(page.getByText('0 thông báo chưa đọc', { exact: true })).toBeVisible(); await expect(page.getByRole('link', { name: 'Thông báo (0)' })).toBeVisible(); await page.reload(); await expect(page.getByText('Không có thông báo phù hợp.')).toBeVisible(); await page.getByLabel('Chỉ xem chưa đọc').uncheck(); await page.getByRole('link', { name: 'Xem đơn hàng', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Đơn ' + order.orderNumber })).toBeVisible()
})

test('Admin management stays separate while the same account can shop, then logout and login to management', async ({ page, browser }) => {
  await customer(page); const customerOrder = await place(page)
  const context = await browser.newContext({ storageState: state }), admin = await context.newPage()
  await admin.goto('/admin/account'); await expect(admin.getByRole('heading', { name: 'Tài khoản quản trị', exact: true })).toBeVisible()
  await expect(admin.getByRole('link', { name: 'Đơn hàng của tôi', exact: true })).toHaveCount(0)
  await expect(admin.getByRole('heading', { name: 'Địa chỉ giao hàng', exact: true })).toHaveCount(0)
  const session = await (await admin.request.get('/api/v1/auth/session')).json() as { email: string; roles: string[] }
  expect(session.roles).toEqual(expect.arrayContaining(['Admin', 'Customer']))
  await expect.poll(async () => ((await (await admin.request.get('/api/v1/admin/notifications?pageSize=100')).json()) as { items: { orderId: string }[] }).items.some(n => n.orderId === customerOrder.id), { timeout: 15000 }).toBe(true)
  expect(((await (await admin.request.get('/api/v1/me/notifications?pageSize=100')).json()) as { items: { orderId: string }[] }).items.some(n => n.orderId === customerOrder.id)).toBe(false)
  await admin.goto('/admin/notifications'); await expect(admin.getByRole('heading', { name: 'Thông báo quản trị', exact: true })).toBeVisible()
  await expect(admin.locator('a[href="/admin/orders/' + customerOrder.id + '"]')).toBeVisible()
  await admin.getByRole('link', { name: '← Mở cửa hàng để mua sắm', exact: true }).click(); await expect(admin).toHaveURL('/')
  const ownOrder = await place(admin)
  await admin.goto('/account/orders'); await expect(admin.getByRole('heading', { name: 'Đơn hàng của tôi' })).toBeVisible(); await expect(admin.locator('.order-row')).toContainText(ownOrder.orderNumber); await expect(admin.locator('.order-row')).not.toContainText(customerOrder.orderNumber)
  await admin.goto('/admin/account')
  for (const width of [360, 768, 1440]) { await admin.setViewportSize({ width, height: 900 }); expect(await admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true) }
  await admin.setViewportSize({ width: 1440, height: 900 }); await admin.getByRole('button', { name: 'Đăng xuất', exact: true }).click(); await expect(admin).toHaveURL(/\/auth\/login(?:\?|$)/); expect((await admin.request.get('/api/v1/auth/session')).status()).toBe(401)
  await admin.goto('/auth/login')
  await admin.getByLabel('Email', { exact: true }).fill(session.email); await admin.getByLabel('Mật khẩu', { exact: true }).fill('Synthetic!Password123'); await admin.getByRole('button', { name: 'Đăng nhập', exact: true }).click(); await expect(admin).toHaveURL('/admin'); await expect(admin.getByRole('heading', { name: 'Tổng quan', exact: true })).toBeVisible()
  await context.close()
})
