import { test, expect } from '@playwright/test'

test('browser calls real API through Vite proxy', async ({ page }) => {
  await page.goto('/')
  const response = page.waitForResponse(r => r.url().endsWith('/api/health'))
  await page.getByRole('button', { name: 'Kiểm tra kết nối backend' }).click()
  expect((await response).status()).toBe(200)
  await expect(page.getByText('PhoneStore API đang hoạt động', { exact: true })).toBeVisible()
  await expect(page.getByRole('button')).toBeEnabled()
})
