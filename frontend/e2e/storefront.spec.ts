import { test, expect } from '@playwright/test'
import type { BrowserContext } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import path from 'node:path'
import { setupAdmin, createProduct, addVariant } from './adminHelpers.js'
import { png } from './pngFixture.js'

if (!/Database=PhoneStore_Test_[0-9a-f]{32};/.test(process.env.PHONESTORE_E2E_SQL ?? '')) throw new Error('Use isolated SQL harness.')
let state: Awaited<ReturnType<BrowserContext['storageState']>>
let product: Awaited<ReturnType<typeof createProduct>>
let newest: Awaited<ReturnType<typeof createProduct>>
let imageUrls: string[]
test.beforeAll(async ({ browser }) => {
  state = await setupAdmin(browser)
  const context = await browser.newContext({ storageState: state }); const page = await context.newPage()
  product = await createProduct(page)
  await addVariant(page, 'PUBLIC-A-' + product.suffix, 'Đen', '25000000', '128')
  await page.getByRole('button', { name: 'Thêm phiên bản mới', exact: true }).click()
  await addVariant(page, 'PUBLIC-B-' + product.suffix, 'Trắng', '26000000', '256')
  const dto = await (await page.request.get('/api/v1/admin/products/' + product.id)).json() as { variants: { id: string; storageGb: number }[] }
  const csrf = (await (await page.request.get('/api/v1/auth/csrf')).json() as { token: string }).token
  const receive = await page.request.post('/api/v1/admin/inventory/' + dto.variants.find(v => v.storageGb === 128)!.id + '/receipts', { data: { quantity: 7, reason: 'Storefront acceptance', operationKey: randomUUID() }, headers: { 'X-CSRF-TOKEN': csrf } })
  expect(receive.status()).toBe(201)
  for (const variant of ['', 'PUBLIC-B-' + product.suffix.toUpperCase()]) {
    await page.getByLabel('File ảnh').setInputFiles({ name: 'synthetic.png', mimeType: 'image/png', buffer: png() })
    if (variant) await page.getByLabel('Ảnh thuộc phiên bản').selectOption({ label: variant })
    else await page.getByLabel('Ảnh thuộc phiên bản').selectOption('')
    await page.getByLabel('Mô tả ảnh').fill(variant ? 'Ảnh trắng riêng' : 'Ảnh chung')
    await page.getByRole('button', { name: 'Tải ảnh lên' }).click()
    await expect(page.getByRole('img', { name: variant ? 'Ảnh trắng riêng' : 'Ảnh chung' })).toBeVisible()
  }
  imageUrls = ((await (await page.request.get('/api/v1/admin/products/' + product.id)).json()) as { images: { imageUrl: string }[] }).images.map(x => x.imageUrl)
  newest = await createProduct(page); await addVariant(page, 'PUBLIC-NEW-' + newest.suffix, 'Xanh')
  await context.close()
})

test('guest home, combined same-variant filters and URL paging use Admin-created SQL data', async ({ page }) => {
  await page.goto('/'); await expect(page.locator('.store-home-catalog')).toContainText(newest.name)
  await page.goto('/products?pageSize=1&sort=newest')
  await expect(page.locator('.store-products')).toContainText(newest.name)
  await page.getByRole('button', { name: 'Trang sau', exact: true }).click()
  await expect(page).toHaveURL(/page=2/); await expect(page.locator('.store-products')).toContainText(product.name)
  await page.reload(); await expect(page.locator('.store-products')).toContainText(product.name)
  await page.getByLabel('Tên điện thoại').fill(product.suffix)
  await page.getByLabel('Hãng', { exact: true }).selectOption({ label: 'Hãng ' + product.suffix })
  await page.getByLabel('Danh mục', { exact: true }).selectOption({ label: 'Danh mục ' + product.suffix })
  await page.getByLabel('Giá tối đa (VND)').fill('25000000')
  await page.getByLabel('Dung lượng (GB)').fill('256')
  await page.getByRole('button', { name: 'Áp dụng bộ lọc' }).click()
  await expect(page.getByRole('heading', { name: 'Không tìm thấy sản phẩm phù hợp' })).toBeVisible()
  await expect(page).not.toHaveURL(/page=2/)
  await page.getByLabel('Giá tối đa (VND)').fill('26000000'); await page.getByLabel('Tình trạng hàng').selectOption('false')
  await page.getByRole('button', { name: 'Áp dụng bộ lọc' }).click()
  await expect(page.locator('.store-products > li')).toHaveCount(1); await expect(page.locator('.store-products')).toContainText('26.000.000')
  await page.reload(); await expect(page.getByLabel('Dung lượng (GB)')).toHaveValue('256')
  for (const width of [360, 768, 1440]) {
    await page.setViewportSize({ width, height: 900 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: path.resolve('../docs/agent-kit/tasks', `P2-04-list-${width}.png`), fullPage: true })
  }
  await page.getByLabel('Số sản phẩm mỗi trang').focus(); await page.keyboard.press('Tab'); await expect(page.getByRole('button', { name: 'Áp dụng bộ lọc' })).toBeFocused()
})

test('guest variant selection has real price/stock, safe description, responsive keyboard and retry', async ({ page }) => {
  await page.goto('/products/phone-' + product.suffix)
  await expect(page.getByRole('heading', { name: product.name, exact: true })).toBeVisible()
  await expect(page.getByRole('radio', { checked: true })).toHaveCount(0); await expect(page.getByRole('button', { name: 'Thêm vào giỏ hàng', exact: true })).toBeDisabled(); await expect(page.locator('.store-detail .store-price')).toContainText('Chọn phiên bản')
  await expect(page.locator('.store-main-photo img')).toHaveAttribute('alt', 'Ảnh chung')
  await expect(page.locator('.store-thumbnails button')).toHaveCount(2)
  await page.getByRole('button', { name: /Ảnh trắng riêng/ }).click()
  await expect(page.locator('.store-main-photo img')).toHaveAttribute('alt', 'Ảnh trắng riêng')
  await expect(page.getByRole('radio', { checked: true })).toHaveCount(0)
  await page.getByRole('button', { name: /Ảnh chung/ }).click()
  await expect(page.locator('.store-description')).toContainText('<script>alert(1)</script>'); await expect(page.locator('.store-description script')).toHaveCount(0)
  await page.getByRole('radio', { name: /Trắng/ }).check(); await expect(page.getByText('Hết hàng', { exact: true })).toBeVisible(); await expect(page.locator('.store-detail .store-price')).toContainText('26.000.000')

  await expect(page.locator('.store-main-photo img')).toHaveAttribute('alt', 'Ảnh trắng riêng')
  await expect(page.locator('.store-thumbnails button')).toHaveCount(2)
  await expect.poll(() => page.locator('.store-main-photo img').evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth > 0)).toBe(true)
  await page.getByRole('radio', { name: /Trắng/ }).focus(); await page.keyboard.press('ArrowLeft'); await expect(page.getByText('Còn 7 sản phẩm')).toBeVisible()
  await expect(page.locator('.store-main-photo img')).toHaveAttribute('alt', 'Ảnh chung')
  for (const width of [360, 768, 1440]) {
    await page.setViewportSize({ width, height: 900 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: path.resolve('../docs/agent-kit/tasks', `P2-04-detail-${width}.png`), fullPage: true })
  }
  const route = '**/api/v1/products/phone-' + product.suffix
  await page.route(route, r => r.abort('failed')); await page.reload(); await expect(page.getByRole('alert')).toBeVisible()
  await page.unroute(route); await page.getByRole('button', { name: 'Thử tải lại' }).click(); await expect(page.getByRole('radio', { checked: true })).toHaveCount(0); await expect(page.getByText('Chọn phiên bản để xem giá')).toBeVisible()
  expect((await page.request.get('/api/v1/admin/products')).status()).toBe(401)
})

test('hiding a product through Admin removes it from guest list/detail after reload', async ({ browser, page }) => {
  const context = await browser.newContext({ storageState: state }); const admin = await context.newPage()
  await admin.goto('/admin/products/' + product.id)
  const panel = admin.locator('.catalog-panel').filter({ has: admin.getByRole('heading', { name: 'Thông tin sản phẩm', exact: true }) })
  await panel.getByLabel('Đang hiển thị').uncheck(); await admin.getByRole('button', { name: 'Lưu sản phẩm', exact: true }).click()
  await expect(admin.getByRole('status').filter({ hasText: 'Đã lưu sản phẩm' })).toBeVisible(); await context.close()
  await page.goto('/products/phone-' + product.suffix); await expect(page.getByRole('heading', { name: 'Không tìm thấy sản phẩm' })).toBeVisible()
  for (const url of imageUrls) expect((await page.request.get(url)).status()).toBe(404)
  await page.goto('/products?search=' + product.suffix); await expect(page.getByRole('heading', { name: 'Không tìm thấy sản phẩm phù hợp' })).toBeVisible()
})
