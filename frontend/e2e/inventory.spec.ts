import { test, expect } from '@playwright/test'
import type { BrowserContext, Page } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import path from 'node:path'
import { setupAdmin, createProduct, addVariant } from './adminHelpers.js'

if (!/Database=PhoneStore_Test_[0-9a-f]{32};/.test(process.env.PHONESTORE_E2E_SQL ?? '')) throw new Error('Use isolated catalog/inventory SQL harness.')
let state: Awaited<ReturnType<BrowserContext['storageState']>>
test.beforeAll(async ({ browser }) => { state = await setupAdmin(browser) })
test.beforeEach(async ({ context }) => { await context.addCookies(state.cookies) })
async function openStock(page: Page) {
  const product = await createProduct(page); const sku = 'INV-' + product.suffix.toUpperCase()
  await addVariant(page, sku, 'Đen')
  await page.goto('/admin/inventory')
  await page.getByLabel('Tìm SKU hoặc tên sản phẩm').fill(sku)
  await page.getByRole('button', { name: 'Tìm kiếm kho' }).click()
  await expect(page.locator('.catalog-list > li')).toHaveCount(1)
  await page.getByRole('link', { name: 'Quản lý kho ' + sku, exact: true }).click()
  await expect(page.getByRole('heading', { name: sku, exact: true })).toBeVisible()
  return { sku, id: page.url().split('/').pop()! }
}
async function write(page: Page, delta: string, reason: string, kind: 'receive' | 'adjust' = 'receive') {
  await page.getByLabel('Loại thao tác').selectOption(kind)
  await page.getByLabel('Số lượng thay đổi', { exact: true }).fill(delta)
  await page.getByLabel('Lý do', { exact: true }).fill(reason)
  await page.getByRole('button', { name: 'Ghi thao tác kho', exact: true }).click()
}
async function stock(page: Page, id: string) { const response = await page.request.get('/api/v1/admin/inventory/' + id); expect(response.status()).toBe(200); return response.json() as Promise<{ onHand: number; reserved: number; available: number }> }

test('real Admin receives, adjusts and reviews inventory with insufficient reduction blocked', async ({ page }) => {
  const { id } = await openStock(page)
  await expect(page.getByText('Chưa có lịch sử kho.')).toBeVisible()
  await write(page, '10', 'Nhập lô hàng thử')
  await expect(page.getByRole('status').filter({ hasText: 'Thao tác kho đã được xác nhận' })).toBeVisible()
  await expect(page.locator('.inventory-movement')).toHaveCount(1)
  expect(await stock(page, id)).toMatchObject({ onHand: 10, reserved: 0, available: 10 })
  await write(page, '-3', 'Điều chỉnh hàng thử', 'adjust')
  await expect(page.locator('.inventory-movement')).toHaveCount(2)
  expect(await stock(page, id)).toMatchObject({ onHand: 7, reserved: 0, available: 7 })
  await page.reload(); await expect(page.locator('.inventory-movement')).toHaveCount(2)
  await write(page, '-8', 'Giảm quá khả dụng', 'adjust')
  await expect(page.getByRole('alert')).toContainText('Không thể giảm quá lượng khả dụng')
  await expect(page.getByRole('status').filter({ hasText: 'Thao tác kho đã được xác nhận' })).toHaveCount(0)
  expect(await stock(page, id)).toMatchObject({ onHand: 7, reserved: 0, available: 7 })
  await expect(page.locator('.inventory-movement')).toHaveCount(2)
  await page.getByRole('button', { name: 'Bỏ thao tác sau khi kiểm tra' }).click()
  await page.getByRole('button', { name: 'Xác nhận đã kiểm tra lịch sử' }).click()
  for (const width of [360, 768, 1440]) {
    await page.setViewportSize({ width, height: 900 })
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: path.resolve('../docs/agent-kit/tasks', `P2-03-stock-${width}.png`), fullPage: true })
  }
  await page.getByLabel('Lý do', { exact: true }).focus(); await page.keyboard.press('Tab')
  await expect(page.getByRole('button', { name: 'Ghi thao tác kho', exact: true })).toBeFocused()
})

test('lost response after SQL commit restores same key after reload and never doubles stock', async ({ page }) => {
  const { id } = await openStock(page)
  let committed = false
  const routePattern = '**/api/v1/admin/inventory/*/receipts'
  await page.route(routePattern, async route => {
    const real = await route.fetch(); expect(real.status()).toBe(201); committed = true
    await route.abort('failed') // Drop only the response; the real authenticated SQL command was committed.
  })
  await write(page, '10', 'Lost response synthetic')
  await expect(page.getByRole('alert')).toContainText('Không thể kết nối')
  expect(committed).toBe(true)
  expect(await stock(page, id)).toMatchObject({ onHand: 10, available: 10 })
  const code = await page.locator('.notice .inventory-key').textContent()
  await page.unroute(routePattern); await page.reload()
  await expect(page.locator('.notice .inventory-key')).toHaveText(code!)
  const replay = page.waitForResponse(r => r.url().endsWith(`/inventory/${id}/receipts`) && r.request().method() === 'POST')
  await page.getByRole('button', { name: 'Thử lại cùng thao tác' }).click()
  const response = await replay; expect(response.status()).toBe(200); expect((await response.json() as { isReplay: boolean }).isReplay).toBe(true)
  await expect(page.getByRole('status').filter({ hasText: 'Thao tác kho đã được xác nhận' })).toBeVisible()
  await expect(page.locator('.inventory-movement')).toHaveCount(1)
  expect(await stock(page, id)).toMatchObject({ onHand: 10, reserved: 0, available: 10 })
})

test('movement paging and direct inventory refresh show committed SQL events', async ({ page }) => {
  const { id } = await openStock(page)
  const csrf = await page.request.get('/api/v1/auth/csrf'); const token = (await csrf.json() as { token: string }).token
  for (let n = 1; n <= 11; n++) {
    const response = await page.request.post(`/api/v1/admin/inventory/${id}/receipts`, { data: { quantity: 1, reason: 'Paging receipt ' + n, operationKey: randomUUID() }, headers: { 'X-CSRF-TOKEN': token } })
    expect(response.status()).toBe(201)
  }
  await page.getByRole('button', { name: 'Tải lại kho và lịch sử' }).click()
  await expect(page.locator('.inventory-movement')).toHaveCount(10)
  await page.getByRole('button', { name: 'Lịch sử sau', exact: true }).click()
  await expect(page.locator('.inventory-movement')).toHaveCount(1)
  await expect(page.locator('.inventory-movement')).toContainText('Paging receipt 1')
  expect(await stock(page, id)).toMatchObject({ onHand: 11, reserved: 0, available: 11 })
  await page.reload(); await expect(page.locator('.inventory-movement')).toHaveCount(10)
})
