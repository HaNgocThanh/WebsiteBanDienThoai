import { selectDestination } from './addressHelpers.js'
import { test, expect } from '@playwright/test'
import type { Page } from '@playwright/test'
import { readdir, readFile } from 'node:fs/promises'
import path from 'node:path'
import { randomUUID } from 'node:crypto'

if (!process.env.PHONESTORE_E2E_MAILBOX || !process.env.PHONESTORE_E2E_SQL) throw new Error('Auth tests require the isolated SQL/mailbox harness; never run against dev DB.')
const password = 'Synthetic!Password123'
const changed = 'Changed!Password456'
interface Mail { UserId: string; Token: string }
async function mailbox(email: string, purpose: string): Promise<Mail> {
  let found: Mail | undefined
  await expect.poll(async () => {
    for (const file of await readdir(process.env.PHONESTORE_E2E_MAILBOX!)) {
      if (!file.endsWith('.json')) continue
      const message: unknown = JSON.parse(await readFile(path.join(process.env.PHONESTORE_E2E_MAILBOX!, file), 'utf8'))
      if (typeof message === 'object' && message !== null && 'Email' in message && message.Email === email && 'Purpose' in message && message.Purpose === purpose
        && 'UserId' in message && typeof message.UserId === 'string' && 'Token' in message && typeof message.Token === 'string') found = { UserId: message.UserId, Token: message.Token }
    }
    return !!found
  }).toBe(true)
  return found!
}
async function register(page: Page, email: string) {
  await page.goto('/auth/register')
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByLabel('Họ tên', { exact: true }).fill('Browser test customer')
  await page.getByLabel('Mật khẩu', { exact: true }).fill(password)
  await page.getByLabel('Nhập lại mật khẩu', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Tạo tài khoản', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('Nếu thông tin phù hợp')
}
async function login(page: Page, email: string, value = password, returnTo = '/account') {
  await page.goto('/auth/login?returnTo=' + encodeURIComponent(returnTo))
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByLabel('Mật khẩu', { exact: true }).fill(value)
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click()
}
async function openLink(page: Page, kind: string, mail: Mail) {
  await page.goto(`/auth/${kind}#userId=${mail.UserId}&token=${encodeURIComponent(mail.Token)}`)
  await expect.poll(() => page.url().includes('#')).toBe(false) // Never print a token in URL assertions.
}

test('real profile and address CRUD survive reload with one explicit default', async ({ page }) => {
  const email = randomUUID() + '@example.invalid'
  await register(page, email)
  await openLink(page, 'verify-email', await mailbox(email, 'verify-email'))
  await page.getByRole('button', { name: 'Xác minh email', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('Email đã được xác minh')
  await login(page, email)
  await expect(page.getByLabel('Email tài khoản')).toHaveValue(email)
  await expect(page.getByLabel('Email tài khoản')).toHaveAttribute('readonly', '')
  await page.getByLabel('Họ tên', { exact: true }).fill('Updated browser customer')
  await page.getByLabel('Số điện thoại hồ sơ').fill('0000000000')
  await page.getByRole('button', { name: 'Lưu hồ sơ', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('Hồ sơ đã được cập nhật')
  await expect(page.getByRole('heading', { name: 'Xin chào, Updated browser customer' })).toBeVisible()
  for (const name of ['Recipient one', 'Recipient two']) {
    await page.getByLabel('Người nhận', { exact: true }).fill(name)
    await page.getByLabel('Điện thoại người nhận').fill('0000000000')
    await page.getByLabel('Số nhà, đường').fill('Synthetic street')
    await selectDestination(page)
    await page.getByLabel('Đặt làm mặc định').check()
    await page.getByRole('button', { name: 'Lưu địa chỉ', exact: true }).click()
    await expect(page.getByRole('status')).toContainText('Địa chỉ đã được lưu')
    await expect(page.getByLabel('Người nhận', { exact: true })).toHaveValue('')
  }
  await page.reload()
  const list = page.locator('.address-list')
  await expect(list.locator('li')).toHaveCount(2)
  await expect(list.getByText('· Mặc định', { exact: true })).toHaveCount(1)
  await page.getByRole('button', { name: 'Sửa Recipient two', exact: true }).click()
  await page.getByLabel('Số nhà, đường').fill('Updated synthetic street')
  await page.getByRole('button', { name: 'Lưu địa chỉ', exact: true }).click()
  await expect(list).toContainText('Updated synthetic street')
  await page.getByRole('button', { name: 'Xóa Recipient two', exact: true }).click()
  await page.getByRole('button', { name: 'Xác nhận xóa', exact: true }).click()
  await expect(list.locator('li')).toHaveCount(1)
  await expect(list.getByText('· Mặc định', { exact: true })).toHaveCount(0)
  await page.reload()
  await expect(list.locator('li')).toHaveCount(1)
  await expect(page.getByLabel('Email tài khoản')).toHaveValue(email)
  await expect(page.getByLabel('Họ tên', { exact: true })).toHaveValue('Updated browser customer')
  for (const width of [360, 768, 1440]) {
    await page.setViewportSize({ width, height: 900 })
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: path.resolve('../docs/agent-kit/tasks', `P1-04-profile-${width}.png`), fullPage: true })
  }
})
test('real register verify login refresh reset and logout with private local mailbox', async ({ page }) => {
  const email = randomUUID() + '@example.invalid'
  await register(page, email)
  await login(page, email)
  await expect(page.getByRole('alert')).toContainText('Vui lòng xác minh email')
  const verification = await mailbox(email, 'verify-email')
  await openLink(page, 'verify-email', verification)
  await page.getByRole('button', { name: 'Xác minh email', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('Email đã được xác minh')
  await login(page, email)
  await expect(page.getByRole('heading', { name: 'Xin chào, Browser test customer' })).toBeVisible()
  await page.reload()
  await expect(page.getByRole('button', { name: 'Đăng xuất', exact: true })).toBeVisible()
  await page.goto('/admin/orders')
  await expect(page.getByRole('heading', { name: 'Bạn không có quyền quản trị' })).toBeVisible()
  await expect(page.getByRole('navigation', { name: 'Điều hướng quản trị' })).toHaveCount(0)
  await page.goto('/auth/forgot-password')
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByRole('button', { name: 'Quên mật khẩu', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('Nếu thông tin phù hợp')
  const reset = await mailbox(email, 'reset-password')
  await openLink(page, 'reset-password', reset)
  await page.getByLabel('Mật khẩu mới', { exact: true }).fill(changed)
  await page.getByLabel('Nhập lại mật khẩu', { exact: true }).fill(changed)
  await page.getByRole('button', { name: 'Đặt lại mật khẩu', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('Mật khẩu đã được cập nhật')
  await login(page, email)
  await expect(page.getByRole('alert')).toContainText('Email hoặc mật khẩu không hợp lệ')
  await login(page, email, changed)
  await expect(page.getByRole('heading', { name: 'Xin chào, Browser test customer' })).toBeVisible()
  expect(await page.evaluate(() => localStorage.length)).toBe(0)
  await page.getByRole('button', { name: 'Đăng xuất', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Đăng nhập', exact: true })).toBeVisible()
  await page.goto('/account')
  await expect(page.getByRole('heading', { name: 'Đăng nhập', exact: true })).toBeVisible()
})
test('reused verification link and invalid reset link offer recovery without success', async ({ page }) => {
  const email = randomUUID() + '@example.invalid'; await register(page, email)
  const message = await mailbox(email, 'verify-email')
  await openLink(page, 'verify-email', message)
  await page.getByRole('button', { name: 'Xác minh email', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('Email đã được xác minh')
  await openLink(page, 'verify-email', message)
  await page.getByRole('button', { name: 'Xác minh email', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('Liên kết không hợp lệ hoặc đã hết hạn')
  await expect(page.getByRole('status')).toHaveCount(0)
  await page.goto('/auth/reset-password')
  await expect(page.getByRole('alert')).toContainText('Liên kết không hợp lệ')
  await expect(page.getByRole('link', { name: 'Yêu cầu liên kết đặt lại mới' })).toBeVisible()
})
for (const width of [360, 768, 1440]) {
  test(`auth forms support keyboard and fit ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 960 })
    await page.goto('/auth/register')
    await page.getByLabel('Email', { exact: true }).focus()
    await page.keyboard.press('Tab'); await expect(page.getByLabel('Họ tên', { exact: true })).toBeFocused()
    await page.getByRole('button', { name: 'Tạo tài khoản', exact: true }).click()
    await expect(page.getByLabel('Email', { exact: true })).toHaveAttribute('aria-invalid', 'true')
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: path.resolve('../docs/agent-kit/tasks', `P1-03-register-${width}.png`), fullPage: true })
  })
}
test('failed login request stays an error and successful session is not fabricated', async ({ page }) => {
  // Test-only failure injection; happy-path above uses real SQL/API entirely.
  await page.route('**/api/v1/auth/login', route => route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ code: 'INTERNAL_ERROR' }) }))
  await login(page, 'synthetic@example.invalid')
  await expect(page.getByRole('alert')).toContainText('Chưa thể hoàn tất yêu cầu')
  await expect(page.getByRole('button', { name: 'Đăng nhập', exact: true })).toBeEnabled()
  await expect(page.getByRole('button', { name: 'Đăng xuất', exact: true })).toHaveCount(0)
})
