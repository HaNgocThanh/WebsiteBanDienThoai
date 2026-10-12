import { test, expect } from '@playwright/test'
import type { BrowserContext, Page } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import path from 'node:path'
import { execFile } from 'node:child_process'
import { promisify } from 'node:util'
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
  expect((await (await page.request.get('/api/v1/admin/orders/' + order.id)).json() as { snapshot: { status: string } }).snapshot.status).toBe('Reviewing')
  const init = await post(page, 'me/orders/' + order.id + '/sepay-checkout'); expect(init.status()).toBe(200)
  const checkout = await init.json() as { invoice: string; amount: number }
  // Synthetic provider notification; never send a transaction to the external gateway.
  const ipn = await page.request.post('/api/v1/payments/sepay/ipn', { headers: { 'X-Secret-Key': 'synthetic-e2e-ipn' }, data: { notification_type: 'ORDER_PAID', order: { order_invoice_number: checkout.invoice, order_status: 'CAPTURED', order_currency: 'VND', order_amount: String(checkout.amount) }, transaction: { id: randomUUID(), transaction_id: randomUUID().replaceAll('-', ''), payment_method: 'BANK_TRANSFER', transaction_type: 'PAYMENT', transaction_status: 'APPROVED', transaction_currency: 'VND', transaction_amount: String(checkout.amount) } } }); expect(ipn.status()).toBe(200)
  expect((await (await page.request.get('/api/v1/admin/orders/' + order.id)).json() as { snapshot: { status: string } }).snapshot.status).toBe('Approved')
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

test('Admin dispatch lost acknowledgment retries once; COD completes only after collection and notifies owner', async ({ browser }) => {
  const context = await browser.newContext({ storageState: state }), page = await context.newPage()
  const { order } = await place(page, 'COD', true); await page.goto('/admin/orders/' + order.id)
  async function step() {
    await page.getByLabel('Lý do cập nhật trạng thái').selectOption({ index: 1 })
    await page.getByLabel('Tôi đã kiểm tra và xác nhận bước xử lý này.').check()
    await page.getByRole('button', { name: /^Chuyển sang/ }).click()
  }
  await expect(page.getByRole('button', { name: 'Chuyển sang Đã xác nhận', exact: true })).toBeVisible(); await step()
  await expect(page.getByRole('button', { name: /^Chuyển sang/ })).toContainText('Đang giao')
  let lost = false, sent: unknown, version: string | undefined
  await page.route('**/api/v1/admin/orders/' + order.id + '/transitions', async route => {
    if (!lost) { sent = route.request().postDataJSON() as unknown; version = route.request().headers()['if-match']; expect((await route.fetch()).status()).toBe(200); lost = true; await route.abort('failed') }
    else { expect(route.request().postDataJSON()).toEqual(sent); expect(route.request().headers()['if-match']).toBe(version); await route.continue() }
  })
  await step(); await page.getByRole('button', { name: 'Thử lại cùng bước xử lý' }).click(); await page.unroute('**/api/v1/admin/orders/' + order.id + '/transitions')
  await expect(page.getByRole('button', { name: /^Chuyển sang/ })).toContainText('Đã giao'); await step()
  await expect(page.locator('.order-lines').locator('..').getByText('Đã giao · Thanh toán khi nhận hàng', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: /^Chuyển sang/ })).toHaveCount(0)
  const reservations = (await (await page.request.get('/api/v1/admin/orders/' + order.id)).json() as { reservations: { status: string }[] }).reservations
  expect(reservations.every(r => r.status === 'Consumed')).toBe(true)
  await page.getByLabel('Ghi chú đối soát', { exact: true }).fill('Synthetic collected cash'); await page.getByLabel('Tôi đã kiểm tra và xác nhận số tiền thực nhận.').check(); await page.getByRole('button', { name: 'Ghi nhận tiền đã nhận', exact: true }).click()
  await expect(page.getByRole('button', { name: /^Chuyển sang/ })).toContainText('Hoàn tất'); await step()
  await expect.poll(async () => (await (await page.request.get('/api/v1/admin/orders/' + order.id)).json() as { snapshot: { status: string } }).snapshot.status).toBe('Completed')
  await expect.poll(async () => ((await (await page.request.get('/api/v1/admin/notifications?pageSize=100')).json()) as { items: { type: string; orderId: string }[] }).items.filter(n => n.type === 'OrderCompleted' && n.orderId === order.id).length).toBe(1)
  for (const width of [360, 768, 1440]) { await page.setViewportSize({ width, height: 900 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true) }
  await page.goto('/account/orders/' + order.id); await expect(page.getByLabel('Lý do cập nhật trạng thái')).toHaveCount(0)
  await context.close()
})

async function saleForm(page: Page, title: string, proof?: string) {
  const form = page.getByRole('heading', { name: title, exact: true }).locator('..')
  await form.getByRole('combobox').selectOption({ index: 1 })
  if (proof) await form.getByRole('textbox', { name: /Chứng từ thu hồi hàng|Mã giao dịch hoàn tiền/ }).fill(proof)
  await form.getByRole('checkbox').check()
  return form
}
test('paid owner cancellation lost acknowledgment releases once; Admin fails and retries refund then records actual completion', async ({ browser }) => {
  test.setTimeout(60000)
  const context = await browser.newContext({ storageState: state }), page = await context.newPage(); const { order } = await place(page, 'BankTransfer', true)
  const checkout = await (await post(page, 'me/orders/' + order.id + '/sepay-checkout')).json() as { invoice: string; amount: number }
  expect((await page.request.post('/api/v1/payments/sepay/ipn', { headers: { 'X-Secret-Key': 'synthetic-e2e-ipn' }, data: { notification_type: 'ORDER_PAID', order: { order_invoice_number: checkout.invoice, order_status: 'CAPTURED', order_currency: 'VND', order_amount: String(checkout.amount) }, transaction: { id: randomUUID(), transaction_id: randomUUID().replaceAll('-', ''), payment_method: 'BANK_TRANSFER', transaction_type: 'PAYMENT', transaction_status: 'APPROVED', transaction_currency: 'VND', transaction_amount: String(checkout.amount) } } })).status()).toBe(200)
  await page.goto('/account/orders/' + order.id); const cancel = await saleForm(page, 'Hủy đơn hàng'); let lost = false, sent: unknown, version: string | undefined
  await page.route('**/api/v1/me/orders/' + order.id + '/cancel', async route => { if (!lost) { sent = route.request().postDataJSON() as unknown; version = route.request().headers()['if-match']; expect((await route.fetch()).status()).toBe(200); lost = true; await route.abort('failed') } else { expect(route.request().postDataJSON()).toEqual(sent); expect(route.request().headers()['if-match']).toBe(version); await route.continue() } })
  await cancel.getByRole('button', { name: 'Hủy đơn hàng', exact: true }).click(); await page.getByRole('button', { name: 'Thử lại: Hủy đơn hàng', exact: true }).click(); await expect(page.getByText('Chờ hoàn tiền', { exact: true })).toBeVisible()
  await page.goto('/admin/orders/' + order.id); let data = await (await page.request.get('/api/v1/admin/orders/' + order.id + '/after-sale')).json() as { refunds: { id: string; status: string }[] }; expect(data.refunds).toHaveLength(1)
  const failTitle = 'Ghi nhận hoàn tiền thất bại #' + data.refunds[0].id; const fail = await saleForm(page, failTitle); await fail.getByRole('button', { name: failTitle, exact: true }).click(); await expect(page.getByRole('heading', { name: /Khoản hoàn .*Hoàn tiền thất bại/ })).toBeVisible()
  const payment = ((await (await page.request.get('/api/v1/admin/orders/' + order.id)).json()) as { payments: { id: string }[] }).payments[0]; const createTitle = 'Tạo yêu cầu hoàn tiền cho khoản #' + payment.id
  const create = await saleForm(page, createTitle); await create.getByLabel('Tiền hàng hoàn (VND)', { exact: true }).fill('1000000'); await create.getByRole('button', { name: createTitle, exact: true }).click()
  await expect(page.getByText('Chờ hoàn tiền', { exact: true })).toBeVisible(); data = await (await page.request.get('/api/v1/admin/orders/' + order.id + '/after-sale')).json() as typeof data; const pending = data.refunds.find(r => r.status === 'Pending')!
  const completeTitle = 'Ghi nhận đã hoàn tiền #' + pending.id; const complete = await saleForm(page, completeTitle, randomUUID()); await complete.getByRole('button', { name: completeTitle, exact: true }).click(); await expect(page.getByText('Đã hoàn tiền', { exact: true })).toBeVisible()
  await expect.poll(async () => ((await (await page.request.get('/api/v1/admin/notifications?pageSize=100')).json()) as { items: { type: string; orderId: string }[] }).items.filter(n => n.type === 'RefundCompleted' && n.orderId === order.id).length).toBe(1)
  await page.goto('/account/orders/' + order.id); await expect(page.getByText('Đã hoàn tiền', { exact: true })).toBeVisible(); await expect(page.getByRole('button', { name: /Ghi nhận đã hoàn tiền/ })).toHaveCount(0)
  await context.close()
})
test('shipping recall requires real goods evidence before Admin restocks and cancels', async ({ browser }) => {
  const context = await browser.newContext({ storageState: state }), page = await context.newPage(); const { order } = await place(page, 'COD', true); await page.goto('/admin/orders/' + order.id)
  async function forward() { await page.getByLabel('Lý do cập nhật trạng thái').selectOption({ index: 1 }); await page.getByLabel('Tôi đã kiểm tra và xác nhận bước xử lý này.').check(); await page.getByRole('button', { name: /^Chuyển sang/ }).click() }
  await forward(); await expect(page.getByRole('button', { name: 'Chuyển sang Đang giao', exact: true })).toBeVisible(); await forward(); await expect(page.getByRole('button', { name: 'Chuyển sang Đã giao', exact: true })).toBeVisible()
  await page.goto('/account/orders/' + order.id); const requestTitle = 'Yêu cầu dừng giao và thu hồi'; const form = await saleForm(page, requestTitle); await form.getByRole('button', { name: requestTitle, exact: true }).click(); await expect(page.getByRole('heading', { name: 'Yêu cầu thu hồi · Đang chờ', exact: true })).toBeVisible()
  await page.goto('/admin/orders/' + order.id); const title = 'Chấp thuận thu hồi và hủy đơn'; const approve = page.getByRole('heading', { name: title, exact: true }).locator('..'); expect(await approve.getByRole('checkbox').isChecked()).toBe(false); expect(await approve.getByLabel('Chứng từ thu hồi hàng').getAttribute('required')).not.toBeNull()
  for (const width of [360, 768, 1440]) { await page.setViewportSize({ width, height: 900 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true) }
  await saleForm(page, title, 'Synthetic recovered receipt'); await approve.getByRole('button', { name: title, exact: true }).click(); await expect(page.getByRole('heading', { name: 'Yêu cầu thu hồi · Đã chấp thuận', exact: true })).toBeVisible()
  const detail = await (await page.request.get('/api/v1/admin/orders/' + order.id)).json() as { snapshot: { status: string }; reservations: { status: string }[] }; expect(detail.snapshot.status).toBe('Cancelled'); expect(detail.reservations.every(r => r.status === 'Consumed')).toBe(true)
  await context.close()
})

test('G4 bank transfer completes through actual Admin UI after verified Sandbox adapter payment', async ({ browser }) => {
  const context = await browser.newContext({ storageState: state }), page = await context.newPage(); const { order } = await place(page, 'BankTransfer', true)
  const checkout = await (await post(page, 'me/orders/' + order.id + '/sepay-checkout')).json() as { invoice: string; amount: number }
  expect((await page.request.post('/api/v1/payments/sepay/ipn', { headers: { 'X-Secret-Key': 'synthetic-e2e-ipn' }, data: { notification_type: 'ORDER_PAID', order: { order_invoice_number: checkout.invoice, order_status: 'CAPTURED', order_currency: 'VND', order_amount: String(checkout.amount) }, transaction: { id: randomUUID(), transaction_id: randomUUID().replaceAll('-', ''), payment_method: 'BANK_TRANSFER', transaction_type: 'PAYMENT', transaction_status: 'APPROVED', transaction_currency: 'VND', transaction_amount: String(checkout.amount) } } })).status()).toBe(200)
  await page.goto('/admin/orders/' + order.id)
  for (const label of ['Đang giao', 'Đã giao', 'Hoàn tất']) {
    const button = page.getByRole('button', { name: 'Chuyển sang ' + label, exact: true }); await expect(button).toBeVisible(); await page.getByLabel('Lý do cập nhật trạng thái').selectOption({ index: 1 }); await page.getByLabel('Tôi đã kiểm tra và xác nhận bước xử lý này.').check(); await button.click()
  }
  await expect(page.getByText('Hoàn tất · Chuyển khoản', { exact: true })).toBeVisible(); await page.goto('/account/orders/' + order.id); await expect(page.getByText('Hoàn tất · Chuyển khoản', { exact: true })).toBeVisible(); await expect(page.getByText('Đã nhận đủ', { exact: true })).toBeVisible(); await context.close()
})
test('G4 real hosted expiry cancels unpaid bank order, releases held stock and sends one notification', async ({ browser }) => {
  const database = /(?:^|;)Database=(PhoneStore_Test_[0-9a-f]{32})(?:;|$)/.exec(process.env.PHONESTORE_E2E_SQL ?? '')?.[1]
  if (!database) throw new Error('Expiry fixture requires isolated GUID test DB')
  const context = await browser.newContext({ storageState: state }), page = await context.newPage(); const { order } = await place(page, 'BankTransfer', true)
  if (!/^[1-9][0-9]*$/.test(order.id)) throw new Error('Invalid fixture order id')
  // Only this synthetic order's clock is moved; production time/policy and worker/API stay real.
  const query = `UPDATE Orders SET PaymentDueAt=DATEADD(SECOND,-1,SYSUTCDATETIME()) WHERE Id=${order.id} AND Status='Reviewing' AND PaymentMethod='BankTransfer'; UPDATE r SET ExpiresAt=DATEADD(SECOND,-1,SYSUTCDATETIME()) FROM StockReservations r JOIN OrderItems i ON i.Id=r.OrderItemId WHERE i.OrderId=${order.id} AND r.Status='Active';`
  await promisify(execFile)('docker', ['compose', 'exec', '-T', 'db', 'sh', '-c', 'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d "$1" -Q "$2"', 'expiry-fixture', database, query], { cwd: path.resolve('..'), timeout: 15000, windowsHide: true })
  await expect.poll(async () => ((await (await page.request.get('/api/v1/admin/orders/' + order.id)).json()) as { snapshot: { status: string } }).snapshot.status, { timeout: 15000 }).toBe('Cancelled')
  const detail = await (await page.request.get('/api/v1/admin/orders/' + order.id)).json() as { reservations: { status: string }[]; refunds: unknown[]; history: { toStatus: string; actorType: string; actorUserId: string | null }[] }
  expect(detail.reservations.every(r => r.status === 'Released')).toBe(true); expect(detail.refunds).toHaveLength(0); const history = detail.history.filter(h => h.toStatus === 'Cancelled'); expect(history).toHaveLength(1); expect(history[0]).toMatchObject({ actorType: 'System', actorUserId: null })
  await expect.poll(async () => ((await (await page.request.get('/api/v1/admin/notifications?pageSize=100')).json()) as { items: { type: string; orderId: string }[] }).items.filter(n => n.type === 'OrderPaymentExpired' && n.orderId === order.id).length, { timeout: 15000 }).toBe(1)
  await page.goto('/account/orders/' + order.id); await expect(page.getByText('Đã hủy · Chuyển khoản', { exact: true })).toBeVisible(); await expect(page.getByRole('button', { name: 'Thanh toán qua SePay Sandbox', exact: true })).toHaveCount(0); await page.reload(); await expect(page.getByText('Đã hủy · Chuyển khoản', { exact: true })).toBeVisible(); await context.close()
})
