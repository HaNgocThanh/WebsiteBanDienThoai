import { expect } from '@playwright/test'
import type { Browser, Page } from '@playwright/test'
import { readdir, readFile } from 'node:fs/promises'
import { execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { randomUUID } from 'node:crypto'
import path from 'node:path'

const password = 'Synthetic!Password123'
async function login(page: Page, email: string) {
  await page.goto('/auth/login?returnTo=/admin/products')
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByLabel('Mật khẩu', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Danh mục sản phẩm', exact: true })).toBeVisible()
}
export async function setupAdmin(browser: Browser) {
  const email = randomUUID() + '@example.invalid'
  const context = await browser.newContext(); const page = await context.newPage()
  await page.goto('/auth/register')
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByLabel('Họ tên', { exact: true }).fill('Catalog browser admin')
  await page.getByLabel('Mật khẩu', { exact: true }).fill(password)
  await page.getByLabel('Nhập lại mật khẩu', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Tạo tài khoản', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('Nếu thông tin phù hợp')
  let mail: { UserId: string; Token: string } | undefined
  await expect.poll(async () => {
    for (const file of await readdir(process.env.PHONESTORE_E2E_MAILBOX!)) {
      const m: unknown = JSON.parse(await readFile(path.join(process.env.PHONESTORE_E2E_MAILBOX!, file), 'utf8'))
      if (typeof m === 'object' && m !== null && 'Email' in m && m.Email === email && 'Purpose' in m && m.Purpose === 'verify-email' && 'UserId' in m && typeof m.UserId === 'string' && 'Token' in m && typeof m.Token === 'string') mail = { UserId: m.UserId, Token: m.Token }
    }
    return !!mail
  }).toBe(true)
  await page.goto(`/auth/verify-email#userId=${mail!.UserId}&token=${encodeURIComponent(mail!.Token)}`)
  await expect.poll(() => page.url().includes('#')).toBe(false)
  await page.getByRole('button', { name: 'Xác minh email', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('Email đã được xác minh')
  // Same documented bootstrap CLI as a local Admin, with an isolated DB and synthetic user only.
  try { await promisify(execFile)('dotnet', ['run', '--project', '../backend/PhoneStore.Api', '--no-launch-profile', '--no-build', '--', '--bootstrap-auth'], { env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', ConnectionStrings__DefaultConnection: process.env.PHONESTORE_E2E_SQL, Auth__BootstrapAdminEmail: email, Logging__LogLevel__Default: 'Warning' }, timeout: 60000, windowsHide: true }) }
  catch { throw new Error('Synthetic Admin bootstrap CLI failed; private process output withheld.') }
  await login(page, email); const state = await context.storageState(); await context.close(); return state
}

export async function createProduct(page: Page) {
  const suffix = randomUUID().replaceAll('-', '').slice(0, 12)
  for (const [route, name] of [['brands', 'Hãng'], ['categories', 'Danh mục']] as const) {
    await page.goto('/admin/' + route)
    await page.getByLabel('Tên', { exact: true }).fill(name + ' ' + suffix)
    await page.getByLabel('Đường dẫn (slug)').fill(route + '-' + suffix)
    await page.getByRole('button', { name: 'Lưu ' + name.toLowerCase(), exact: true }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Đã lưu thay đổi' })).toBeVisible()
  }
  await page.goto('/admin/products/new')
  await page.getByLabel('Tên sản phẩm').fill('Điện thoại ' + suffix)
  await page.getByLabel('Đường dẫn (slug)').fill('phone-' + suffix)
  await page.getByLabel('Hãng', { exact: true }).selectOption({ label: 'Hãng ' + suffix })
  await page.getByLabel('Danh mục', { exact: true }).selectOption({ label: 'Danh mục ' + suffix })
  await page.getByLabel('Mô tả', { exact: true }).fill('<script>alert(1)</script> mô tả bằng text')
  await page.getByRole('button', { name: 'Lưu sản phẩm', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Chỉnh sửa sản phẩm' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Phiên bản', exact: true })).toBeVisible()
  return { suffix, id: page.url().split('/').pop()!, name: 'Điện thoại ' + suffix }
}
export async function addVariant(page: Page, sku: string, color: string, price = '25000000', storage = '128') {
  await page.getByLabel('SKU', { exact: true }).fill(sku)
  await page.getByLabel('Màu', { exact: true }).fill(color)
  await page.getByLabel('Giá (VND)').fill(price)
  await page.getByLabel('Dung lượng (GB)').fill(storage)
  await page.getByRole('button', { name: 'Lưu phiên bản', exact: true }).click()
  await expect(page.getByRole('status').filter({ hasText: 'Đã lưu phiên bản' })).toBeVisible()
}
