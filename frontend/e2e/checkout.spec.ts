import { test, expect } from '@playwright/test'
import type { Page, BrowserContext } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import { readdir, readFile } from 'node:fs/promises'
import path from 'node:path'
import { setupAdmin, createProduct, addVariant } from './adminHelpers.js'

if (!/Database=PhoneStore_Test_[0-9a-f]{32};/.test(process.env.PHONESTORE_E2E_SQL ?? '')) throw new Error('Use isolated GUID SQL harness.')
let state: Awaited<ReturnType<BrowserContext['storageState']>>, variantId: string
async function post(page: Page, path: string, body?: unknown, key?: string) {
  const token = (await (await page.request.get('/api/v1/auth/csrf')).json() as { token: string }).token
  return page.request.post('/api/v1/' + path, { ...(body ? { data: body } : {}), headers: { 'X-CSRF-TOKEN': token, ...(key ? { 'Idempotency-Key': key } : {}) } })
}
test.beforeAll(async ({ browser }) => {
  state = await setupAdmin(browser); const context = await browser.newContext({ storageState: state }), page = await context.newPage()
  const product = await createProduct(page); await addVariant(page, 'CHECKOUT-' + product.suffix, 'Đen')
  const dto = await (await page.request.get('/api/v1/admin/products/' + product.id)).json() as { variants: { id: string }[] }; variantId = dto.variants[0].id
  expect((await post(page, `admin/inventory/${variantId}/receipts`, { quantity: 5, reason: 'Synthetic checkout browser', operationKey: randomUUID() })).status()).toBe(201)
  await context.close()
})
async function body(page: Page, paymentMethod: 'COD' | 'BankTransfer') {
  const catalog = await (await page.request.get('/api/v1/locations/provinces/79/wards')).json() as { items: { code: string; name: string }[] }, ward = catalog.items[0]
  const address = { addressLine: 'Synthetic street', province: 'Thành phố Hồ Chí Minh', provinceCode: '79', wardCode: ward.code, locality: ward.name, countryCode: 'VN' }
  const items = [{ variantId, quantity: 1 }]
  const quote = await post(page, 'checkout/quote', { items, shippingAddress: address }); expect(quote.status()).toBe(200)
  const quoteHash = (await quote.json() as { quoteHash: string }).quoteHash
  return { ...address, items, quoteHash, paymentMethod, recipientName: 'Synthetic recipient', phone: '0000000000' }
}
test('guest JS fetch creates one COD order with protected cookie and can safely replay after reload', async ({ page, context }) => {
  await page.goto('/cart')
  const session = await page.evaluate(async () => {
    const csrf = await (await fetch('/api/v1/auth/csrf', { credentials: 'same-origin' })).json() as { token: string }
    const response = await fetch('/api/v1/checkout/sessions', { method: 'POST', credentials: 'same-origin', headers: { 'X-CSRF-TOKEN': csrf.token } })
    if (!response.ok) throw new Error('Session failed')
    return await response.json() as { checkoutKey: string; expiresAt: string }
  })
  const cookie = (await context.cookies(new URL('/api/v1/', page.url()).href)).find(c => c.name === 'PhoneStore.CheckoutGuest')!
  expect(cookie.httpOnly).toBe(true); expect(cookie.sameSite).toBe('Strict'); expect(cookie.path).toBe('/api/v1')
  const payload = { ...await body(page, 'COD'), email: 'synthetic-guest@example.invalid', createAccountConsent: false, consentTextVersion: 'account-create-v1' }
  const response = await page.evaluate(async ({ key, payload }) => {
    const csrf = await (await fetch('/api/v1/auth/csrf', { credentials: 'same-origin' })).json() as { token: string }
    const response = await fetch('/api/v1/orders', { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrf.token, 'Idempotency-Key': key }, body: JSON.stringify(payload) })
    return { status: response.status, order: await response.json() as { id: string; status: string; grandTotal: number; paymentDueAt: string | null } }
  }, { key: session.checkoutKey, payload })
  expect(response.status).toBe(201); expect(response.order).toMatchObject({ status: 'Placed', grandTotal: 25000000, paymentDueAt: null }); expect(typeof response.order.id).toBe('string')
  await page.reload(); const retry = await post(page, 'orders', payload, session.checkoutKey); expect(retry.status()).toBe(200); expect(await retry.json()).toEqual(response.order)
  const altered = await post(page, 'orders', { ...payload, note: 'Different retry' }, session.checkoutKey); expect(altered.status()).toBe(409); expect((await altered.json() as { code: string }).code).toBe('IDEMPOTENCY_PAYLOAD_MISMATCH')
  expect(await page.evaluate(() => Object.keys(localStorage))).not.toContain('checkoutKey')
})
test('logged-in customer checkout returns BankTransfer due time and accepts exact replay with server account email', async ({ browser }) => {
  const context = await browser.newContext({ storageState: state }), page = await context.newPage(); await page.goto('/')
  const sessionResponse = await post(page, 'checkout/sessions'); expect(sessionResponse.status()).toBe(200); const session = await sessionResponse.json() as { checkoutKey: string }
  const payload = await body(page, 'BankTransfer'); const start = Date.now(); const response = await post(page, 'orders', payload, session.checkoutKey); expect(response.status()).toBe(201)
  const order = await response.json() as { id: string; paymentMethod: string; paymentDueAt: string }
  expect(order.paymentMethod).toBe('BankTransfer'); expect(Date.parse(order.paymentDueAt)).toBeGreaterThanOrEqual(start + 24 * 60 * 60 * 1000 - 1000)
  expect(Date.parse(order.paymentDueAt)).toBeLessThanOrEqual(Date.now() + 24 * 60 * 60 * 1000)
  const retry = await post(page, 'orders', payload, session.checkoutKey); expect(retry.status()).toBe(200); expect(await retry.json()).toEqual(order)
  expect((await context.cookies()).some(c => c.name === 'PhoneStore.CheckoutGuest')).toBe(false)
  await context.close()
})

test('hosted outbox delivers guest access, browser cookie survives reload and verified customer claims exact order', async ({ page, context }) => {
  test.setTimeout(90000)
  await page.goto('/cart'); const email = randomUUID() + '@example.invalid'
  const session = await (await post(page, 'checkout/sessions')).json() as { checkoutKey: string }
  const placed = await post(page, 'orders', { ...await body(page, 'BankTransfer'), email, createAccountConsent: false, consentTextVersion: 'account-create-v1' }, session.checkoutKey)
  expect(placed.status()).toBe(201); const order = await placed.json() as { id: string; orderNumber: string }
  async function token(purpose: string) {
    let found: string | undefined
    await expect.poll(async () => {
      const directory = path.join(process.env.PHONESTORE_E2E_MAILBOX!, 'orders')
      for (const file of await readdir(directory).catch((): string[] => [])) {
        if (!file.endsWith('.json')) continue
        const mail = JSON.parse(await readFile(path.join(directory, file), 'utf8')) as { Email: string; OrderNumber: string; Purpose: string; ActionPath: string | null }
        if (mail.Email === email && mail.OrderNumber === order.orderNumber && mail.Purpose === purpose && mail.ActionPath) found = new URLSearchParams(mail.ActionPath.split('#')[1]).get('token') ?? undefined
      }
      return Boolean(found)
    }, { timeout: 30000 }).toBe(true)
    return found!
  }
  const viewToken = await token('ViewOrder')
  expect((await post(page, 'guest/order-access/exchange', { token: viewToken, purpose: 'ViewOrder' })).status()).toBe(200)
  const cookie = (await context.cookies()).find(c => c.name === 'PhoneStore.GuestOrder')!
  expect(cookie.httpOnly).toBe(true); expect(cookie.sameSite).toBe('Strict'); expect(cookie.path).toBe('/api/v1/guest')
  await page.reload()
  const viewed = await page.evaluate(async () => {
    const response = await fetch('/api/v1/guest/order'); return { status: response.status, data: await response.json() as { id: string; paymentMethod: string } }
  })
  expect(viewed.status).toBe(200); expect(viewed.data.id).toBe(order.id); expect(viewed.data.paymentMethod).toBe('BankTransfer')
  expect((await post(page, 'guest/account-setup-requests')).status()).toBe(403)
  expect((await post(page, 'guest/order-access-requests', { orderNumber: order.orderNumber, email, purpose: 'ClaimOrder' })).status()).toBe(202)
  const claimToken = await token('ClaimOrder')
  expect((await post(page, 'auth/register', { email, password: 'Synthetic!Password123', fullName: 'Synthetic guest claim' })).status()).toBe(202)
  let verification: { UserId: string; Token: string } | undefined
  await expect.poll(async () => {
    for (const file of await readdir(process.env.PHONESTORE_E2E_MAILBOX!)) {
      if (!file.endsWith('.json')) continue
      const mail = JSON.parse(await readFile(path.join(process.env.PHONESTORE_E2E_MAILBOX!, file), 'utf8')) as { Email: string; Purpose: string; UserId: string; Token: string }
      if (mail.Email === email && mail.Purpose === 'verify-email') verification = { UserId: mail.UserId, Token: mail.Token }
    }
    return Boolean(verification)
  }).toBe(true)
  expect((await post(page, 'auth/verify-email', verification)).status()).toBe(204)
  expect((await post(page, 'auth/login', { email, password: 'Synthetic!Password123' })).status()).toBe(204)
  const claimed = await post(page, 'me/order-claims', { token: claimToken }); expect(claimed.status()).toBe(200); expect((await claimed.json() as { id: string }).id).toBe(order.id)
  expect((await post(page, 'me/order-claims', { token: claimToken })).status()).toBe(200)
  expect((await page.request.get('/api/v1/guest/order')).status()).toBe(401)
})
