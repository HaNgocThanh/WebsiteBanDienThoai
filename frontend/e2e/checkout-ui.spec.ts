import { test, expect } from '@playwright/test'
import type { BrowserContext, Page } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import { readdir, readFile } from 'node:fs/promises'
import path from 'node:path'
import { setupAdmin, createProduct, addVariant } from './adminHelpers.js'
import { selectDestination } from './addressHelpers.js'

if (!/Database=PhoneStore_Test_[0-9a-f]{32};/.test(process.env.PHONESTORE_E2E_SQL ?? '')) throw new Error('Use isolated GUID SQL harness.')
let state: Awaited<ReturnType<BrowserContext['storageState']>>, product: Awaited<ReturnType<typeof createProduct>>, variantId: string
async function post(page: Page, route: string, data: unknown) {
  const csrf = (await (await page.request.get('/api/v1/auth/csrf')).json() as { token: string }).token
  return page.request.post('/api/v1/' + route, { data, headers: { 'X-CSRF-TOKEN': csrf } })
}
test.beforeAll(async ({ browser }) => {
  state = await setupAdmin(browser); const context = await browser.newContext({ storageState: state }), page = await context.newPage()
  product = await createProduct(page); await addVariant(page, 'CHECKOUT-UI-' + product.suffix, 'Đen')
  variantId = ((await (await page.request.get('/api/v1/admin/products/' + product.id)).json()) as { variants: { id: string }[] }).variants[0].id
  expect((await post(page, `admin/inventory/${variantId}/receipts`, { quantity: 40, reason: 'Synthetic checkout UI', operationKey: randomUUID() })).status()).toBe(201); await context.close()
})
async function start(page: Page, email?: string, province = '79', suffix = product.suffix) {
  await page.goto('/products/phone-' + suffix); await page.getByRole('button', { name: 'Thêm vào giỏ hàng', exact: true }).click(); await page.getByRole('link', { name: 'Xem giỏ hàng' }).click(); await page.getByRole('link', { name: 'Tiến hành đặt hàng' }).click()
  await page.getByLabel('Họ tên người nhận', { exact: true }).fill('Synthetic recipient'); await page.getByLabel('Số điện thoại', { exact: true }).fill('0000000000')
  if (email) await page.getByLabel('Email nhận đơn', { exact: true }).fill(email)
  await selectDestination(page, province)
}
async function quote(page: Page) { await page.getByRole('button', { name: 'Tính báo giá', exact: true }).click(); await expect(page.getByLabel('Tôi xác nhận sản phẩm, số lượng và tổng tiền trên.')).toBeVisible() }
async function place(page: Page) { await page.getByLabel('Tôi xác nhận sản phẩm, số lượng và tổng tiền trên.').check(); await page.getByRole('button', { name: 'Đặt hàng', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Đặt hàng thành công', exact: true })).toBeVisible() }
async function orderMail(email: string, purpose: string) {
  let result: { ActionPath: string; OrderNumber: string } | undefined
  await expect.poll(async () => {
    const directory = path.join(process.env.PHONESTORE_E2E_MAILBOX!, 'orders')
    for (const file of await readdir(directory).catch((): string[] => [])) {
      if (!file.endsWith('.json')) continue
      const m = JSON.parse(await readFile(path.join(directory, file), 'utf8')) as { Email: string; Purpose: string; ActionPath: string; OrderNumber: string }
      if (m.Email === email && m.Purpose === purpose && m.ActionPath) result = m
    }
    return Boolean(result)
  }, { timeout: 30000 }).toBe(true)
  return result!
}
async function authMail(email: string) {
  let result: { UserId: string; Token: string } | undefined
  await expect.poll(async () => {
    for (const file of await readdir(process.env.PHONESTORE_E2E_MAILBOX!)) {
      if (!file.endsWith('.json')) continue
      const m = JSON.parse(await readFile(path.join(process.env.PHONESTORE_E2E_MAILBOX!, file), 'utf8')) as { Email: string; Purpose: string; UserId: string; Token: string }
      if (m.Email === email && m.Purpose === 'verify-email') result = m
    }
    return Boolean(result)
  }).toBe(true); return result!
}
test('guest checkout, scoped email view, consent setup and claim finish through actual UI', async ({ page, context }) => {
  test.setTimeout(90000); const email = randomUUID() + '@example.invalid'; await start(page, email, '48')
  const consent = page.getByLabel('Tôi đồng ý nhận hướng dẫn tạo tài khoản', { exact: false }); await expect(consent).not.toBeChecked(); await consent.check(); await page.getByLabel('Chuyển khoản ngân hàng', { exact: true }).check(); await quote(page)
  await expect(page.locator('.checkout-summary')).toContainText('25.030.000'); await expect(page.getByRole('button', { name: 'Đặt hàng', exact: true })).toBeDisabled()
  for (const width of [360, 768, 1440]) { await page.setViewportSize({ width, height: 900 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true); await page.screenshot({ path: path.resolve('../docs/agent-kit/tasks', `P3-04-checkout-${width}.png`), fullPage: true }) }
  await place(page); await expect(page.locator('.order-receipt')).toContainText('Hạn thanh toán'); await page.reload(); await expect(page.getByRole('heading', { name: 'Đặt hàng thành công', exact: true })).toBeVisible()
  expect(await page.evaluate(() => localStorage.getItem('phonestore.cart.v1'))).toBe('[]')
  const mail = await orderMail(email, 'ViewOrder'); await page.goto(mail.ActionPath); await expect.poll(() => new URL(page.url()).hash === '').toBe(true)
  await page.getByRole('button', { name: 'Xác thực và xem đơn', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Đơn hàng khách vãng lai', exact: true })).toBeVisible(); await expect(page.locator('.order-lines')).toContainText(product.name)
  const cookie = (await context.cookies()).find(c => c.name === 'PhoneStore.GuestOrder')!; expect(cookie.httpOnly).toBe(true); await page.reload(); await expect(page.getByRole('heading', { name: 'Sản phẩm đã đặt', exact: true })).toBeVisible()
  for (const width of [360, 768, 1440]) { await page.setViewportSize({ width, height: 900 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true); await page.screenshot({ path: path.resolve('../docs/agent-kit/tasks', `P3-04-guest-${width}.png`), fullPage: true }) }
  await page.getByRole('button', { name: 'Gửi hướng dẫn tạo tài khoản', exact: true }).click(); await expect(page.getByRole('status').filter({ hasText: 'Nếu thông tin phù hợp' })).toBeVisible(); const claim = await orderMail(email, 'ClaimOrder')
  await page.goto('/auth/register'); await page.getByLabel('Email', { exact: true }).fill(email); await page.getByLabel('Họ tên', { exact: true }).fill('Synthetic guest'); await page.getByLabel('Mật khẩu', { exact: true }).fill('Synthetic!Password123'); await page.getByLabel('Nhập lại mật khẩu', { exact: true }).fill('Synthetic!Password123'); await page.getByRole('button', { name: 'Tạo tài khoản', exact: true }).click(); await expect(page.getByRole('status')).toContainText('Nếu thông tin phù hợp')
  const verification = await authMail(email); await page.goto(`/auth/verify-email#userId=${verification.UserId}&token=${encodeURIComponent(verification.Token)}`); await expect.poll(() => new URL(page.url()).hash === '').toBe(true); await page.getByRole('button', { name: 'Xác minh email', exact: true }).click(); await expect(page.getByRole('status')).toContainText('Email đã được xác minh')
  await page.goto(claim.ActionPath); await expect.poll(() => new URL(page.url()).hash === '').toBe(true); await page.getByRole('link', { name: 'Đăng nhập để nhận đơn', exact: true }).click(); await page.getByLabel('Email', { exact: true }).fill(email); await page.getByLabel('Mật khẩu', { exact: true }).fill('Synthetic!Password123'); await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Nhận đơn vào tài khoản', exact: true })).toBeVisible(); await page.getByRole('button', { name: 'Nhận đơn vào tài khoản', exact: true }).click(); await expect(page.getByRole('status')).toContainText('đã được nhận vào tài khoản')
  await page.goto('/guest/order'); await expect(page.getByRole('alert')).toContainText('Phiên xem đơn đã hết hạn'); await expect(page.getByRole('link', { name: 'Đăng nhập', exact: true })).toHaveCount(0)
})
test('account checkout uses verified readonly email and COD, without guest consent', async ({ browser }) => {
  const context = await browser.newContext({ storageState: state }), page = await context.newPage(); await start(page); await expect(page.getByLabel('Email nhận đơn')).toHaveAttribute('readonly', ''); await expect(page.getByLabel('Tôi đồng ý nhận hướng dẫn tạo tài khoản', { exact: false })).toHaveCount(0); await quote(page); await place(page); await expect(page.locator('.order-receipt')).toContainText('Thanh toán khi nhận hàng'); await expect(page.locator('.order-receipt')).not.toContainText('Hạn thanh toán'); await context.close()
})
test('lost order response recovers after reload without new order or PII storage', async ({ page, browser }) => {
  const admin = await browser.newContext({ storageState: state }), adminPage = await admin.newPage(); const before = await (await adminPage.request.get('/api/v1/admin/inventory/' + variantId)).json() as { reserved: number }
  const email = randomUUID() + '@example.invalid'; await start(page, email); await quote(page); await page.getByLabel('Tôi xác nhận sản phẩm, số lượng và tổng tiền trên.').check()
  let posts = 0, committedId = ''
  await page.route('**/api/v1/orders', async route => { posts++; const response = await route.fetch(); expect(response.status()).toBe(201); committedId = (await response.json() as { id: string }).id; await route.abort('failed') })
  await page.getByRole('button', { name: 'Đặt hàng', exact: true }).click(); await expect(page.getByRole('button', { name: 'Thử lại yêu cầu đặt hàng', exact: true })).toBeVisible(); await expect(page.getByLabel('Email nhận đơn')).toBeDisabled(); expect(posts).toBe(1)
  const keys = await page.evaluate(() => Object.keys(JSON.parse(sessionStorage.getItem('phonestore.checkout.v1')!))); expect(keys).toEqual(['key', 'items']); expect(await page.evaluate(value => Object.values(sessionStorage).some(v => typeof v === 'string' && v.includes(value)), email)).toBe(false)
  await page.reload(); await expect(page.getByRole('heading', { name: 'Đặt hàng thành công', exact: true })).toBeVisible(); expect(posts).toBe(1)
  const operation = await page.evaluate(() => JSON.parse(sessionStorage.getItem('phonestore.checkout.v1')!) as { key: string }); const result = await page.request.get(`/api/v1/checkout/sessions/${operation.key}/result`); expect((await result.json() as { order: { id: string } }).order.id).toBe(committedId)
  const inventory = await adminPage.request.get('/api/v1/admin/inventory/' + variantId); expect(inventory.status()).toBe(200); expect((await inventory.json() as { reserved: number }).reserved).toBe(before.reserved + 1); await admin.close()
})
test('checkout stale price requires new quote and explicit reconfirmation, using the same session', async ({ page, browser }) => {
  const email = randomUUID() + '@example.invalid'; await start(page, email); await quote(page); await page.getByLabel('Tôi xác nhận sản phẩm, số lượng và tổng tiền trên.').check()
  const admin = await browser.newContext({ storageState: state }), adminPage = await admin.newPage(); await adminPage.goto('/')
  const v = ((await (await adminPage.request.get('/api/v1/admin/products/' + product.id)).json()) as { variants: { sku: string; color: string; storageGb: number; ramGb: number; isActive: boolean; version: string }[] }).variants[0]
  const csrf = (await (await adminPage.request.get('/api/v1/auth/csrf')).json() as { token: string }).token
  expect((await adminPage.request.patch('/api/v1/admin/variants/' + variantId, { data: { sku: v.sku, color: v.color, storageGb: v.storageGb, ramGb: v.ramGb, price: 27000000, isActive: v.isActive }, headers: { 'X-CSRF-TOKEN': csrf, 'If-Match': '"' + v.version + '"' } })).status()).toBe(200); await admin.close()
  const keys: string[] = []; page.on('request', request => { if (new URL(request.url()).pathname === '/api/v1/orders') keys.push(request.headers()['idempotency-key']) })
  await page.getByRole('button', { name: 'Đặt hàng', exact: true }).click(); await expect(page.getByRole('alert')).toContainText('Giá hoặc phí đã thay đổi'); await expect(page.getByRole('button', { name: 'Đặt hàng', exact: true })).toBeDisabled(); await quote(page); await expect(page.locator('.checkout-summary')).toContainText('27.000.000'); await expect(page.getByLabel('Tôi xác nhận sản phẩm, số lượng và tổng tiền trên.')).not.toBeChecked(); await place(page); expect(keys.length).toBe(2); expect(keys[1]).toBe(keys[0])
})
test('lookup is neutral and invalid email token offers recovery with scrubbed URL', async ({ page }) => {
  await page.goto('/guest/lookup'); await page.getByLabel('Mã đơn hàng', { exact: true }).fill('missing'); await page.getByLabel('Email đặt hàng', { exact: true }).fill('synthetic@example.invalid'); await page.getByRole('button', { name: 'Gửi liên kết qua email', exact: true }).click(); await expect(page.getByRole('status')).toContainText('Nếu thông tin phù hợp')
  await page.goto('/guest/access#token=' + '0'.repeat(64) + '&purpose=ViewOrder'); await expect.poll(() => new URL(page.url()).hash === '').toBe(true); await page.getByRole('button', { name: 'Xác thực và xem đơn', exact: true }).click(); await expect(page.getByRole('alert')).toContainText('Liên kết không hợp lệ'); await expect(page.getByRole('link', { name: 'Gửi lại liên kết truy cập', exact: true })).toBeVisible()
  await page.reload(); await expect(page.getByRole('heading', { name: 'Cần liên kết từ email', exact: true })).toBeVisible()
})
test('two guest checkout UIs competing for final stock show exactly one success', async ({ browser }) => {
  const admin = await browser.newContext({ storageState: state }), adminPage = await admin.newPage(); const last = await createProduct(adminPage); await addVariant(adminPage, 'LAST-UI-' + last.suffix, 'Đen')
  const id = ((await (await adminPage.request.get('/api/v1/admin/products/' + last.id)).json()) as { variants: { id: string }[] }).variants[0].id
  expect((await post(adminPage, `admin/inventory/${id}/receipts`, { quantity: 1, reason: 'Synthetic final stock UI', operationKey: randomUUID() })).status()).toBe(201)
  const a = await browser.newContext(), b = await browser.newContext(), first = await a.newPage(), second = await b.newPage()
  await Promise.all([start(first, randomUUID() + '@example.invalid', '79', last.suffix), start(second, randomUUID() + '@example.invalid', '79', last.suffix)])
  await Promise.all([quote(first), quote(second)]); await first.getByLabel('Tôi xác nhận sản phẩm, số lượng và tổng tiền trên.').check(); await second.getByLabel('Tôi xác nhận sản phẩm, số lượng và tổng tiền trên.').check()
  await Promise.all([first.getByRole('button', { name: 'Đặt hàng', exact: true }).click(), second.getByRole('button', { name: 'Đặt hàng', exact: true }).click()])
  await expect.poll(async () => (await first.getByRole('heading', { name: 'Đặt hàng thành công', exact: true }).count()) + (await second.getByRole('heading', { name: 'Đặt hàng thành công', exact: true }).count())).toBe(1)
  const losing = await first.getByRole('heading', { name: 'Đặt hàng thành công', exact: true }).count() ? second : first
  await expect(losing.getByRole('alert')).toContainText('vượt lượng còn hàng'); await expect(losing.getByRole('button', { name: 'Đặt hàng', exact: true })).toBeDisabled()
  const inventory = await (await adminPage.request.get('/api/v1/admin/inventory/' + id)).json() as { onHand: number; reserved: number; available: number }; expect(inventory).toMatchObject({ onHand: 1, reserved: 1, available: 0 })
  await a.close(); await b.close(); await admin.close()
})
