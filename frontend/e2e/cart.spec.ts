import { selectDestination } from './addressHelpers.js'
import { test, expect } from '@playwright/test'
import type { BrowserContext, Page } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import path from 'node:path'
import { png } from './pngFixture.js'
import { setupAdmin, createProduct, addVariant } from './adminHelpers.js'

if (!/Database=PhoneStore_Test_[0-9a-f]{32};/.test(process.env.PHONESTORE_E2E_SQL ?? '')) throw new Error('Use isolated GUID SQL harness.')
let state: Awaited<ReturnType<BrowserContext['storageState']>>
let product: Awaited<ReturnType<typeof createProduct>>
let id: string
let imageUrl: string
const cartKey = 'phonestore.cart.v1'
test.beforeAll(async ({ browser }) => {
  state = await setupAdmin(browser); const context = await browser.newContext({ storageState: state }); const page = await context.newPage()
  product = await createProduct(page); await addVariant(page, 'CART-' + product.suffix, 'Đen')
  const dto = await (await page.request.get('/api/v1/admin/products/' + product.id)).json() as { variants: { id: string }[] }; id = dto.variants[0].id
  const token = (await (await page.request.get('/api/v1/auth/csrf')).json() as { token: string }).token
  const upload = await page.request.post(`/api/v1/admin/products/${product.id}/images`, { headers: { 'X-CSRF-TOKEN': token }, multipart: { file: { name: 'synthetic.png', mimeType: 'image/png', buffer: png() }, variantId: id, altText: 'Synthetic selected variant', sortOrder: '0' } })
  expect(upload.status()).toBe(201); imageUrl = (await upload.json() as { imageUrl: string }).imageUrl
  expect((await page.request.post(`/api/v1/admin/inventory/${id}/receipts`, { data: { quantity: 5, reason: 'Synthetic cart acceptance', operationKey: randomUUID() }, headers: { 'X-CSRF-TOKEN': token } })).status()).toBe(201)
  await context.close()
})
async function add(page: Page) {
  await page.goto('/products/phone-' + product.suffix)
  await page.getByRole('button', { name: 'Thêm vào giỏ hàng', exact: true }).click()
  await expect(page.getByRole('status').filter({ hasText: 'Đã thêm phiên bản' })).toBeVisible(); await page.getByRole('link', { name: 'Xem giỏ hàng' }).click()
}
async function quote(page: Page, province: string) { await selectDestination(page, province === 'Đà Nẵng' ? '48' : '79'); await page.getByRole('button', { name: 'Tính lại báo giá' }).click(); await expect(page.getByRole('heading', { name: 'Báo giá hiện tại' })).toBeVisible() }

test('guest cart survives reload, quotes true HCM/other fees and quantity; SQL stock is unchanged', async ({ page, browser }) => {
  await add(page)
  await expect(page.locator('.cart-items').getByRole('heading', { name: product.name })).toBeVisible()
  const image = page.locator('.cart-image img'); await expect(image).toHaveAttribute('src', imageUrl)
  await expect.poll(() => image.evaluate((element: HTMLImageElement) => element.complete && element.naturalWidth > 0)).toBe(true)
  await expect(page.locator('.cart-quote')).toHaveCount(0)
  await quote(page, 'TP. Hồ Chí Minh')
  await expect(page.locator('.cart-quote')).toContainText('25.000.000'); await expect(page.locator('.cart-quote dl > div').filter({ hasText: 'Phí giao hàng' })).toContainText('0 ₫')
  await page.getByRole('button', { name: 'Xác nhận báo giá hiện tại' }).click(); await expect(page.getByRole('status').filter({ hasText: 'đã xác nhận' })).toBeVisible()
  await quote(page, 'Đà Nẵng'); await expect(page.locator('.cart-quote')).toContainText('25.030.000'); await expect(page.getByRole('status').filter({ hasText: 'đã xác nhận' })).toHaveCount(0)
  await page.getByRole('spinbutton').fill('2'); await expect(page.locator('.cart-quote')).toHaveCount(0); await page.getByRole('spinbutton').press('Enter')
  await quote(page, 'Đà Nẵng'); await expect(page.locator('.cart-quote')).toContainText('50.030.000')
  expect(await page.evaluate(key => JSON.parse(localStorage.getItem(key)!), cartKey)).toEqual([{ variantId: id, quantity: 2 }])
  await page.getByRole('button', { name: /^Tăng số lượng / }).click()
  await page.getByRole('spinbutton').press('Enter')
  expect(await page.evaluate(key => JSON.parse(localStorage.getItem(key)!), cartKey)).toEqual([{ variantId: id, quantity: 3 }])
  await page.getByRole('spinbutton').press('ArrowDown')
  await page.getByRole('spinbutton').press('Enter')
  expect(await page.evaluate(key => JSON.parse(localStorage.getItem(key)!), cartKey)).toEqual([{ variantId: id, quantity: 2 }])
  await page.getByLabel('Tỉnh/thành phố', { exact: true }).selectOption('79')
  await expect(page.getByLabel('Phường/xã')).toHaveValue('')
  await page.reload(); await expect(page.getByRole('spinbutton')).toHaveValue('2'); await expect(page.locator('.cart-quote')).toHaveCount(0)
  await expect(page.locator('.cart-items').getByRole('heading', { name: product.name })).toBeVisible(); await expect(page.locator('.cart-image img')).toHaveAttribute('src', imageUrl)
  await quote(page, 'Đà Nẵng')
  for (const width of [360, 768, 1440]) {
    await page.setViewportSize({ width, height: 900 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    const remove = page.locator('.cart-remove'), heading = page.locator('.cart-item-heading h2')
    const buttonBox = (await remove.boundingBox())!, headingBox = (await heading.boundingBox())!
    expect(buttonBox.x).toBeGreaterThan(headingBox.x + headingBox.width); expect(Math.abs(buttonBox.y - headingBox.y)).toBeLessThan(5)
    await remove.focus(); await expect(remove).toBeFocused()
    await remove.hover(); await expect(remove).toHaveCSS('background-color', 'rgb(197, 43, 54)')
    await page.screenshot({ path: path.resolve('../docs/agent-kit/tasks', `P3-01-cart-${width}.png`), fullPage: true })
  }
  await page.getByLabel('Phường/xã').focus(); await page.keyboard.press('Tab'); await expect(page.getByRole('button', { name: 'Tính lại báo giá' })).toBeFocused()
  const context = await browser.newContext({ storageState: state }); const admin = await context.newPage(); await admin.goto('/')
  const stock = await (await admin.request.get('/api/v1/admin/inventory/' + id)).json() as { onHand: number; reserved: number; available: number }
  expect(stock).toMatchObject({ onHand: 5, reserved: 0, available: 5 }); await context.close()
  await page.locator('.cart-remove').click(); await expect(page.getByRole('heading', { name: 'Giỏ hàng đang trống' })).toBeVisible()
})

test('real Admin price update forces PRICE_CHANGED and explicit new confirmation', async ({ page, browser }) => {
  await add(page); await quote(page, 'Hồ Chí Minh'); await page.getByRole('button', { name: 'Xác nhận báo giá hiện tại' }).click()
  const context = await browser.newContext({ storageState: state }); const admin = await context.newPage(); await admin.goto('/')
  const productDto = await (await admin.request.get('/api/v1/admin/products/' + product.id)).json() as { variants: { id: string; sku: string; color: string; storageGb: number; ramGb: number; price: number; isActive: boolean; version: string }[] }
  const v = productDto.variants[0], token = (await (await admin.request.get('/api/v1/auth/csrf')).json() as { token: string }).token
  const response = await admin.request.patch('/api/v1/admin/variants/' + id, { data: { sku: v.sku, color: v.color, storageGb: v.storageGb, ramGb: v.ramGb, price: 24000000, isActive: v.isActive }, headers: { 'X-CSRF-TOKEN': token, 'If-Match': '"' + v.version + '"' } }); expect(response.status()).toBe(200); await context.close()
  const conflict = page.waitForResponse(r => r.url().endsWith('/checkout/quote') && r.status() === 409)
  await page.getByRole('button', { name: 'Tính lại báo giá' }).click(); expect((await (await conflict).json() as { code: string }).code).toBe('PRICE_CHANGED')
  await expect(page.getByText('Giá hoặc phí đã thay đổi. Xem báo giá mới và xác nhận lại.')).toBeVisible(); await expect(page.locator('.cart-quote')).toContainText('24.000.000'); await expect(page.getByRole('status').filter({ hasText: 'đã xác nhận' })).toHaveCount(0)
  await page.getByRole('button', { name: 'Xác nhận báo giá hiện tại' }).click(); await expect(page.getByRole('status').filter({ hasText: 'đã xác nhận' })).toBeVisible()
})

test('stored price/tier tamper is stripped, network retry and out-of-stock never falsely confirm', async ({ page }) => {
  await page.goto('/'); await page.evaluate(({ key, variantId }) => localStorage.setItem(key, JSON.stringify([{ variantId, quantity: 1, unitPrice: 0, tier: 'Diamond' }])), { key: cartKey, variantId: id }); await page.goto('/cart')
  let body: unknown
  await page.route('**/api/v1/checkout/quote', async route => { body = route.request().postDataJSON() as unknown; await route.abort('failed') })
  await selectDestination(page, '01'); await page.getByRole('button', { name: 'Tính lại báo giá' }).click(); await expect(page.getByRole('alert')).toContainText('Không thể kết nối'); await expect(page.locator('.cart-quote')).toHaveCount(0)
  expect(body).toMatchObject({ items: [{ variantId: id, quantity: 1 }], shippingAddress: { provinceCode: '01', province: 'Thành phố Hà Nội', addressLine: 'Synthetic street', countryCode: 'VN' } })
  await page.unroute('**/api/v1/checkout/quote'); await page.getByRole('button', { name: 'Tính lại báo giá' }).click(); await expect(page.locator('.cart-quote')).toContainText('24.030.000')
  await page.getByRole('spinbutton').fill('6'); await page.getByRole('spinbutton').press('Enter'); await page.getByRole('button', { name: 'Tính lại báo giá' }).click()
  await expect(page.getByRole('alert')).toContainText('vượt lượng còn hàng'); await expect(page.getByRole('button', { name: 'Xác nhận báo giá hiện tại' })).toHaveCount(0)
})

test('another tab updating or corrupting cart invalidates the confirmed quote and offers recovery', async ({ page, context }) => {
  await add(page); await quote(page, 'HCM'); await page.getByRole('button', { name: 'Xác nhận báo giá hiện tại' }).click()
  const other = await context.newPage(); await other.goto('/health')
  await other.evaluate(({ key, variantId }) => localStorage.setItem(key, JSON.stringify([{ variantId, quantity: 2 }])), { key: cartKey, variantId: id })
  await expect(page.getByRole('spinbutton')).toHaveValue('2'); await expect(page.locator('.cart-quote')).toHaveCount(0)
  await quote(page, 'HCM'); await expect(page.locator('.cart-quote')).toContainText('48.000.000'); await expect(page.getByRole('status').filter({ hasText: 'đã xác nhận' })).toHaveCount(0)
  await other.evaluate(key => localStorage.setItem(key, '{broken'), cartKey)
  await expect(page.getByRole('alert')).toContainText('Không thể đọc hoặc lưu giỏ hàng'); await expect(page.locator('.cart-quote')).toHaveCount(0)
  await page.getByRole('button', { name: 'Xóa giỏ để khôi phục' }).click(); await expect(page.getByRole('heading', { name: 'Giỏ hàng đang trống' })).toBeVisible(); expect(await page.evaluate(key => localStorage.getItem(key), cartKey)).toBe('[]'); await other.close()
})
test('each cart variant has its own name/image and right-side trash button removes only that variant', async ({ page, browser }) => {
  const context = await browser.newContext({ storageState: state }), admin = await context.newPage()
  await admin.goto('/admin/products/' + product.id)
  await expect(admin.getByRole('heading', { name: 'Thêm phiên bản', exact: true })).toBeVisible()
  await addVariant(admin, 'CART-BLUE-' + product.suffix, 'Xanh', '26000000', '256')
  const dto = await (await admin.request.get('/api/v1/admin/products/' + product.id)).json() as { variants: { id: string; color: string }[] }
  const second = dto.variants.find(v => v.color === 'Xanh')!.id
  const token = (await (await admin.request.get('/api/v1/auth/csrf')).json() as { token: string }).token
  const upload = await admin.request.post(`/api/v1/admin/products/${product.id}/images`, { headers: { 'X-CSRF-TOKEN': token }, multipart: { file: { name: 'synthetic-blue.png', mimeType: 'image/png', buffer: png() }, variantId: second, altText: 'Synthetic blue variant', sortOrder: '0' } })
  expect(upload.status()).toBe(201); const secondImage = (await upload.json() as { imageUrl: string }).imageUrl
  await context.close()
  await page.goto('/'); await page.evaluate(({ key, first, second }) => localStorage.setItem(key, JSON.stringify([{ variantId: first, quantity: 1 }, { variantId: second, quantity: 2 }])), { key: cartKey, first: id, second }); await page.goto('/cart')
  const rows = page.locator('.cart-items > li'); await expect(rows).toHaveCount(2)
  await expect(rows.nth(0)).toContainText('Đen · 128 GB'); await expect(rows.nth(1)).toContainText('Xanh · 256 GB')
  await expect(rows.nth(0).locator('img')).toHaveAttribute('src', imageUrl); await expect(rows.nth(1).locator('img')).toHaveAttribute('src', secondImage)
  await rows.nth(1).getByRole('button', { name: /^Xóa / }).click(); await expect(rows).toHaveCount(1)
  expect(await page.evaluate(key => JSON.parse(localStorage.getItem(key)!), cartKey)).toEqual([{ variantId: id, quantity: 1 }])
  await expect(rows.nth(0)).toContainText('Đen · 128 GB')
})
