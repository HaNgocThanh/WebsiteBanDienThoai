import { test, expect } from '@playwright/test'
import type { BrowserContext, Page } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import path from 'node:path'
import { setupAdmin, createProduct, addVariant } from './adminHelpers.js'
let state: Awaited<ReturnType<BrowserContext['storageState']>>, variantId: string
async function post(page: Page, route: string, data?: unknown, headers = {}) { const csrf = (await (await page.request.get('/api/v1/auth/csrf')).json() as { token: string }).token; return page.request.post('/api/v1/' + route, { data, headers: { 'X-CSRF-TOKEN': csrf, ...headers } }) }
test.beforeAll(async ({ browser }) => {
  state = await setupAdmin(browser); const context = await browser.newContext({ storageState: state }), page = await context.newPage(); const product = await createProduct(page); await addVariant(page, 'PAYMENT-' + product.suffix, 'Đen', '1000000')
  variantId = ((await (await page.request.get('/api/v1/admin/products/' + product.id)).json()) as { variants: { id: string }[] }).variants[0].id
  expect((await post(page, 'admin/inventory/' + variantId + '/receipts', { quantity: 10, reason: 'Synthetic payments', operationKey: randomUUID() })).status()).toBe(201); await context.close()
})
async function place(page: Page, method: string, account = false) {
  await page.goto('/'); const session = await (await post(page, 'checkout/sessions')).json() as { checkoutKey: string }; const items = [{ variantId, quantity: 1 }]
  const quote = await (await post(page, 'checkout/quote', { items, shippingAddress: { province: 'Thành phố Hồ Chí Minh', countryCode: 'VN' } })).json() as { quoteHash: string }
  const response = await post(page, 'orders', { items, ...(account ? {} : { email: randomUUID() + '@example.invalid', consentTextVersion: 'account-create-v1' }), recipientName: 'Synthetic payment guest', phone: '0000000000', addressLine: 'Synthetic street', province: 'Thành phố Hồ Chí Minh', countryCode: 'VN', paymentMethod: method, quoteHash: quote.quoteHash, createAccountConsent: false }, { 'Idempotency-Key': session.checkoutKey }); expect(response.status()).toBe(201)
  return { order: await response.json() as { id: string }, key: session.checkoutKey }
}
test('guest receipt launches signed Sandbox form; authenticated provider fixture updates SQL; redirects cannot pay', async ({ page }) => {
  const { order, key } = await place(page, 'BankTransfer')
  await page.evaluate(({ key, variantId }) => sessionStorage.setItem('phonestore.checkout.v1', JSON.stringify({ key, items: [{ variantId, quantity: 1 }] })), { key, variantId })
  await page.goto('/checkout/success'); await expect(page.getByRole('heading', { name: 'Đặt hàng thành công', exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Thanh toán qua SePay Sandbox', exact: true }).click(); const button = page.getByRole('button', { name: 'Tiếp tục sang SePay Sandbox' }); await expect(button).toBeVisible(); const form = button.locator('..')
  expect(await form.getAttribute('action')).toBe('https://pay-sandbox.sepay.vn/v1/checkout/init'); expect(await form.getAttribute('method')).toBe('post'); const invoice = await form.locator('input[name="order_invoice_number"]').inputValue(); const amount = await form.locator('input[name="order_amount"]').inputValue()
  // Provider adapter fixture only: no request is sent to external SePay; API authentication/SQL remain real.
  await page.goto('/payments/sepay/result?orderId=' + order.id + '&result=success'); await expect(page.getByText('Đã nhận đủ', { exact: true })).toHaveCount(0); await expect(page.locator('.payment-panel [role=status]').filter({ hasText: 'Đang chờ thanh toán' })).toBeVisible()
  const ipn = await page.request.post('/api/v1/payments/sepay/ipn', { headers: { 'X-Secret-Key': 'synthetic-e2e-ipn' }, data: { notification_type: 'ORDER_PAID', order: { order_invoice_number: invoice, order_status: 'CAPTURED', order_currency: 'VND', order_amount: amount }, transaction: { id: randomUUID(), transaction_id: randomUUID().replaceAll('-', ''), payment_method: 'BANK_TRANSFER', transaction_type: 'PAYMENT', transaction_status: 'APPROVED', transaction_currency: 'VND', transaction_amount: amount } } }); expect(ipn.status()).toBe(200)
  await page.getByRole('button', { name: 'Kiểm tra lại trạng thái tiền', exact: true }).click(); await expect(page.getByText('Đã nhận đủ', { exact: true })).toBeVisible(); await expect(page.getByRole('button', { name: 'Thanh toán qua SePay Sandbox', exact: true })).toHaveCount(0)
  for (const width of [360, 768, 1440]) { await page.setViewportSize({ width, height: 900 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true) }
  await page.goto('/checkout/success'); await expect(page.getByRole('heading', { name: 'Đặt hàng thành công', exact: true })).toBeVisible(); await expect(page.getByText('Đã nhận đủ', { exact: true })).toBeVisible()
})
test('paid bank transfer order remains readable by its owner and Admin after the deadline is cleared', async ({ browser }) => {
  const context = await browser.newContext({ storageState: state }), page = await context.newPage()
  const { order } = await place(page, 'BankTransfer', true)
  const init = await post(page, 'me/orders/' + order.id + '/sepay-checkout'); expect(init.status()).toBe(200)
  const checkout = await init.json() as { invoice: string; amount: number }
  // Synthetic provider notification; never send a transaction to the external gateway.
  const ipn = await page.request.post('/api/v1/payments/sepay/ipn', { headers: { 'X-Secret-Key': 'synthetic-e2e-ipn' }, data: { notification_type: 'ORDER_PAID', order: { order_invoice_number: checkout.invoice, order_status: 'CAPTURED', order_currency: 'VND', order_amount: String(checkout.amount) }, transaction: { id: randomUUID(), transaction_id: randomUUID().replaceAll('-', ''), payment_method: 'BANK_TRANSFER', transaction_type: 'PAYMENT', transaction_status: 'APPROVED', transaction_currency: 'VND', transaction_amount: String(checkout.amount) } } }); expect(ipn.status()).toBe(200)
  for (const area of ['account', 'admin']) {
    await page.goto('/' + area + '/orders/' + order.id)
    await expect(page.getByRole('heading', { name: 'Đơn hàng đã đặt', exact: true })).toBeVisible()
    await expect(page.getByText('Đã nhận đủ', { exact: true })).toBeVisible()
    await expect(page.getByText('Hạn thanh toán:', { exact: false })).toHaveCount(0)
    await page.reload()
    await expect(page.getByRole('heading', { name: 'Thông tin nhận hàng', exact: true })).toBeVisible()
  }
  await context.close()
})
test('Admin COD lost-response retry produces one receipt and payment notification', async ({ browser }) => {
  const context = await browser.newContext({ storageState: state }), page = await context.newPage(); const { order } = await place(page, 'COD', true); await page.goto('/admin/orders/' + order.id)
  await page.getByLabel('Ghi chú đối soát', { exact: true }).fill('Synthetic verified cash'); await page.getByLabel('Tôi đã kiểm tra và xác nhận số tiền thực nhận.').check()
  let sent: unknown, lost = false
  await page.route('**/api/v1/admin/orders/' + order.id + '/cod-receipts', async route => { if (!lost) { sent = route.request().postDataJSON() as unknown; expect((await route.fetch()).status()).toBe(200); lost = true; await route.abort('failed') } else { expect(route.request().postDataJSON()).toEqual(sent); await route.continue() } })
  await page.getByRole('button', { name: 'Ghi nhận tiền đã nhận', exact: true }).click(); await page.getByRole('button', { name: 'Thử lại cùng yêu cầu thanh toán', exact: true }).click(); await expect(page.getByText('Đã nhận đủ', { exact: true })).toBeVisible(); await expect(page.locator('.payment-row')).toHaveCount(1)
  await expect.poll(async () => { const notices = await (await page.request.get('/api/v1/admin/notifications?pageSize=100')).json() as { items: { type: string; orderId: string }[] }; return notices.items.filter(n => n.orderId === order.id && n.type === 'PaymentConfirmed').length }).toBe(1)
  for (const width of [360, 768, 1440]) { await page.setViewportSize({ width, height: 900 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true); if (width === 360) await page.screenshot({ path: path.resolve('../docs/agent-kit/tasks/P4-02-COD-360.png'), fullPage: true }) }
  await context.close()
})
