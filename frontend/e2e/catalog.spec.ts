import { setupAdmin, createProduct, addVariant } from './adminHelpers.js'
import { test, expect } from '@playwright/test'
import type { Page, BrowserContext } from '@playwright/test'
import { png } from './pngFixture.js'
import path from 'node:path'

if (!/Database=PhoneStore_Test_[0-9a-f]{32};/.test(process.env.PHONESTORE_E2E_SQL ?? '') || !process.env.PHONESTORE_E2E_MAILBOX || !process.env.PHONESTORE_E2E_IMAGES) throw new Error('Catalog tests require the isolated GUID SQL/mailbox/image harness.')
let state: Awaited<ReturnType<BrowserContext['storageState']>>
test.beforeAll(async ({ browser }) => { state = await setupAdmin(browser) })
test.beforeEach(async ({ context }) => { await context.addCookies(state.cookies) })

async function mutate(page: Page, url: string, data: unknown, version?: string) {
  const csrf = await page.request.get('/api/v1/auth/csrf'); const token = (await csrf.json() as { token: string }).token
  return page.request.patch(url, { data, headers: { 'X-CSRF-TOKEN': token, ...(version ? { 'If-Match': `"${version}"` } : {}) } })
}
test('Admin creates two variants, edits the intended one, uploads/deletes PNG and hides via real API', async ({ page }) => {
  const product = await createProduct(page)
  const sku1 = 'A-' + product.suffix.toUpperCase(), sku2 = 'B-' + product.suffix.toUpperCase()
  await addVariant(page, sku1, 'Đen')
  await page.getByRole('button', { name: 'Thêm phiên bản mới', exact: true }).click()
  await addVariant(page, sku2, 'Trắng', '26000000', '256')
  await page.getByRole('button', { name: 'Sửa ' + sku1, exact: true }).click()
  await page.getByLabel('Giá (VND)').fill('24500000')
  await page.getByRole('button', { name: 'Lưu phiên bản', exact: true }).click()
  await expect(page.locator('.catalog-row').filter({ hasText: sku1 })).toContainText('24.500.000')
  await expect(page.locator('.catalog-row').filter({ hasText: sku2 })).toContainText('26.000.000')
  const saved = await page.request.get('/api/v1/admin/products/' + product.id)
  const dto = await saved.json() as { variants: { id: string; sku: string; price: number }[] }
  expect(dto.variants.find(v => v.sku === sku1)?.price).toBe(24500000)
  expect(dto.variants.find(v => v.sku === sku2)?.price).toBe(26000000)
  await page.getByLabel('File ảnh').setInputFiles({ name: 'test.png', mimeType: 'image/png', buffer: png() })
  await page.getByLabel('Ảnh thuộc phiên bản').selectOption({ label: sku2 })
  await page.getByLabel('Mô tả ảnh').fill('Ảnh phiên bản trắng')
  await page.getByRole('button', { name: 'Tải ảnh lên' }).click()
  await expect(page.getByRole('status').filter({ hasText: 'Đã thêm ảnh' })).toBeVisible()
  const image = page.getByRole('img', { name: 'Ảnh phiên bản trắng' })
  await expect(image).toBeVisible()
  await expect.poll(() => image.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth > 0)).toBe(true)
  const downloaded = page.waitForEvent('download')
  await page.getByRole('link', { name: 'Tải ảnh Ảnh phiên bản trắng', exact: true }).click()
  expect((await downloaded).suggestedFilename()).toMatch(/^[0-9a-f]{32}\.png$/)
  for (const width of [360, 768, 1440]) {
    await page.setViewportSize({ width, height: 900 })
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: path.resolve('../docs/agent-kit/tasks', `P2-02-product-${width}.png`), fullPage: true })
  }
  await page.getByRole('button', { name: 'Xóa ảnh Ảnh phiên bản trắng', exact: true }).click()
  await page.getByRole('button', { name: 'Xác nhận xóa ảnh', exact: true }).click()
  await expect(image).toHaveCount(0)
  const productPanel = page.locator('.catalog-panel').filter({ has: page.getByRole('heading', { name: 'Thông tin sản phẩm', exact: true }) })
  await productPanel.getByLabel('Đang hiển thị').uncheck()
  await page.getByRole('button', { name: 'Lưu sản phẩm', exact: true }).click()
  await expect(page.getByRole('status').filter({ hasText: 'Đã lưu sản phẩm' })).toBeVisible()
  await page.reload(); await expect(productPanel.getByLabel('Đang hiển thị')).not.toBeChecked()
  expect((await page.request.get('/api/v1/products/phone-' + product.suffix)).status()).toBe(404)
})

test('unique and stale product/variant errors preserve drafts without false success or automatic retry', async ({ page }) => {
  const p = await createProduct(page); const sku = 'RV-' + p.suffix.toUpperCase()
  await addVariant(page, sku, 'Đen')
  await page.getByRole('button', { name: 'Thêm phiên bản mới', exact: true }).click()
  await page.getByLabel('SKU', { exact: true }).fill(sku); await page.getByLabel('Màu', { exact: true }).fill('Trắng'); await page.getByLabel('Giá (VND)').fill('123')
  await page.getByRole('button', { name: 'Lưu phiên bản', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('đã tồn tại')
  await expect(page.getByRole('status').filter({ hasText: 'Đã lưu phiên bản' })).toHaveCount(0)
  await expect(page.getByLabel('Màu', { exact: true })).toHaveValue('Trắng')
  const dto = await (await page.request.get('/api/v1/admin/products/' + p.id)).json() as { name: string; slug: string; brandId: string; categoryId: string; description: string; specificationsJson: null; isActive: boolean; version: string; variants: { id: string; sku: string; color: string; price: number; storageGb: number; ramGb: number; isActive: boolean; version: string }[] }
  const { variants, version } = dto
  const body = { name: 'Tên từ người khác', slug: dto.slug, brandId: dto.brandId, categoryId: dto.categoryId, description: dto.description, specificationsJson: dto.specificationsJson, isActive: dto.isActive }
  expect((await mutate(page, '/api/v1/admin/products/' + p.id, body, version)).status()).toBe(200)
  await page.getByLabel('Tên sản phẩm').fill('Bản nháp của tôi')
  await page.getByRole('button', { name: 'Lưu sản phẩm', exact: true }).click()
  await expect(page.getByRole('alert').filter({ hasText: 'Tải phiên bản mới' })).toBeVisible()
  await expect(page.getByLabel('Tên sản phẩm')).toHaveValue('Bản nháp của tôi')
  await page.getByRole('button', { name: 'Tải phiên bản mới (bỏ thay đổi chưa lưu)', exact: true }).click()
  await expect(page.getByLabel('Tên sản phẩm')).toHaveValue('Tên từ người khác')
  await page.getByRole('button', { name: 'Sửa ' + sku, exact: true }).click()
  const v = variants[0]
  expect((await mutate(page, '/api/v1/admin/variants/' + v.id, { sku: v.sku, color: v.color, price: 999, storageGb: v.storageGb, ramGb: v.ramGb, isActive: v.isActive }, v.version)).status()).toBe(200)
  await page.getByLabel('Giá (VND)').fill('888')
  await page.getByRole('button', { name: 'Lưu phiên bản', exact: true }).click()
  await expect(page.getByRole('alert').filter({ hasText: 'Tải phiên bản mới' })).toBeVisible()
  await expect(page.getByLabel('Giá (VND)')).toHaveValue('888')
  await page.getByRole('button', { name: 'Tải phiên bản mới (bỏ thay đổi chưa lưu)', exact: true }).click()
  await expect(page.getByLabel('Giá (VND)')).toHaveValue('999')
  await page.getByLabel('Giá (VND)').fill('1000'); await page.getByRole('button', { name: 'Lưu phiên bản', exact: true }).click()
  await expect(page.getByRole('status').filter({ hasText: 'Đã lưu phiên bản' })).toBeVisible()
})

test('Admin filters/pagination persist in URL and parent hide/edit survive reload', async ({ page }) => {
  const first = await createProduct(page); const second = await createProduct(page)
  await page.goto('/admin/products?pageSize=1')
  await expect(page.locator('.catalog-list > li')).toHaveCount(1)
  await expect(page.locator('.catalog-list')).toContainText(second.name)
  await page.getByRole('button', { name: 'Trang sau', exact: true }).click()
  await expect(page).toHaveURL(/page=2/); await expect(page.locator('.catalog-list')).toContainText(first.name)
  await page.reload(); await expect(page.locator('.catalog-list')).toContainText(first.name)
  for (const width of [360, 768, 1440]) {
    await page.setViewportSize({ width, height: 900 })
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: path.resolve('../docs/agent-kit/tasks', `P2-02-list-${width}.png`), fullPage: true })
  }
  await page.getByLabel('Tìm theo tên').focus(); await page.keyboard.press('Tab')
  await expect(page.getByRole('button', { name: 'Tìm kiếm', exact: true })).toBeFocused()
  await page.getByLabel('Tìm theo tên').fill(second.suffix); await page.getByRole('button', { name: 'Tìm kiếm', exact: true }).click()
  await expect(page.locator('.catalog-list')).toContainText(second.name); await expect(page).not.toHaveURL(/page=2/)
  await page.getByLabel('Trạng thái', { exact: true }).selectOption('false')
  await expect(page.getByRole('heading', { name: 'Không có sản phẩm phù hợp' })).toBeVisible()
  await page.getByRole('button', { name: 'Xóa bộ lọc' }).click()
  await page.getByLabel('Hãng', { exact: true }).selectOption({ label: 'Hãng ' + first.suffix })
  await page.getByLabel('Danh mục', { exact: true }).selectOption({ label: 'Danh mục ' + first.suffix })
  await expect(page.locator('.catalog-list')).toContainText(first.name); await expect(page.locator('.catalog-list > li')).toHaveCount(1)
  await page.goto('/admin/brands')
  await page.getByRole('button', { name: 'Sửa Hãng ' + first.suffix, exact: true }).click()
  await page.getByLabel('Đang hiển thị').uncheck(); await page.getByRole('button', { name: 'Lưu hãng', exact: true }).click()
  await expect(page.getByRole('status').filter({ hasText: 'Đã lưu thay đổi' })).toBeVisible(); await page.reload()
  await expect(page.locator('.catalog-row').filter({ hasText: 'Hãng ' + first.suffix })).toContainText('Đã ẩn')
  for (const width of [360, 768, 1440]) { await page.setViewportSize({ width, height: 900 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true) }
  await page.setViewportSize({ width: 360, height: 900 })
  await page.getByRole('button', { name: 'Mở menu quản trị' }).click(); await page.keyboard.press('Escape')
  await expect(page.getByRole('button', { name: 'Mở menu quản trị' })).toBeFocused()
  await page.goto('/admin/products/' + first.id)
  await page.getByLabel('Tên sản phẩm').fill('Không thể bật cha ẩn')
  await page.getByRole('button', { name: 'Lưu sản phẩm', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('Hãng, danh mục hoặc sản phẩm đang ẩn')
  await expect(page.getByRole('status').filter({ hasText: 'Đã lưu sản phẩm' })).toHaveCount(0)
})

test('lost upload acknowledgment retries once and reload recovers the same image through real API', async ({ page }) => {
  const product = await createProduct(page)
  const route = `**/api/v1/admin/products/${product.id}/images`
  let firstKey = ''
  await page.route(route, async request => {
    const body = request.request().postDataBuffer()?.toString() ?? ''
    firstKey = /name="operationKey"\r\n\r\n([^\r]+)/.exec(body)?.[1] ?? ''
    const response = await request.fetch(); expect(response.status()).toBe(201)
    await request.abort('failed')
  })
  await page.getByLabel('File ảnh').setInputFiles({ name: 'retry.png', mimeType: 'image/png', buffer: png() })
  await page.getByLabel('Mô tả ảnh').fill('Ảnh mất phản hồi')
  await page.getByRole('button', { name: 'Tải ảnh lên', exact: true }).click()
  await expect(page.getByRole('alert')).toBeVisible(); await expect(page.getByLabel('Mô tả ảnh')).toBeDisabled()
  expect(firstKey).toMatch(/^[0-9a-f-]{36}$/)
  let retryKey = ''
  await page.unroute(route)
  await page.route(route, async request => {
    retryKey = /name="operationKey"\r\n\r\n([^\r]+)/.exec(request.request().postDataBuffer()?.toString() ?? '')?.[1] ?? ''
    await request.continue()
  })
  await page.getByRole('button', { name: 'Thử lại tải ảnh', exact: true }).click()
  await expect(page.getByRole('status').filter({ hasText: 'Đã thêm ảnh' })).toBeVisible(); expect(retryKey).toBe(firstKey)
  const dto = await (await page.request.get('/api/v1/admin/products/' + product.id)).json() as { images: { id: string }[] }; expect(dto.images).toHaveLength(1)
  await page.unroute(route)
  await page.route(route, async request => { expect((await request.fetch()).status()).toBe(201); await request.abort('failed') })
  await page.getByLabel('File ảnh').setInputFiles({ name: 'reload.png', mimeType: 'image/png', buffer: png() })
  await page.getByLabel('Mô tả ảnh').fill('Ảnh phục hồi sau tải lại')
  await page.getByRole('button', { name: 'Tải ảnh lên', exact: true }).click()
  await expect(page.getByRole('alert')).toBeVisible(); await page.unroute(route); await page.reload()
  await page.getByRole('button', { name: 'Kiểm tra kết quả tải ảnh', exact: true }).click()
  await expect(page.getByRole('status').filter({ hasText: 'Đã tìm thấy ảnh' })).toBeVisible()
  await expect(page.locator('.catalog-images > li')).toHaveCount(2)
  const final = await (await page.request.get('/api/v1/admin/products/' + product.id)).json() as { images: { id: string }[] }; expect(final.images).toHaveLength(2); expect(final.images[0].id).toBe(dto.images[0].id)
  await expect(page.getByRole('button', { name: 'Tải ảnh lên', exact: true })).toBeEnabled()
})
