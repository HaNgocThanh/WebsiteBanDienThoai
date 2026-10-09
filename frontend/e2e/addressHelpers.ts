import type { Page } from '@playwright/test'
import { expect } from '@playwright/test'

export async function selectDestination(page: Page, provinceCode = '79') {
  await page.getByLabel('Số nhà, đường').fill('Synthetic street')
  const province = page.getByLabel('Tỉnh/thành phố', { exact: true })
  await expect(province).toBeEnabled(); await province.selectOption(provinceCode)
  const ward = page.getByLabel('Phường/xã', { exact: true })
  await expect(ward).toBeEnabled()
  await ward.selectOption({ index: 1 })
}
