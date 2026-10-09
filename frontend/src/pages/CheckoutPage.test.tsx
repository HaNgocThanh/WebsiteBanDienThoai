import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AuthContext } from '../auth/AuthContext'
import { CartProvider } from '../cart/CartProvider'
import { cartStorageKey } from '../cart/CartContext'
import { pendingCheckoutStorageKey, savePendingCheckout } from '../checkout/pending'
import { quotes } from '../services/quotes'
import type { Quote } from '../services/quotes'
import { orders } from '../services/orders'
import type { PlacedOrder } from '../services/orders'
import { ApiError } from '../services/errors'
import { mockLocations } from '../test/locationFixtures'
import { CheckoutPage, CheckoutSuccessPage } from './CheckoutPage'

const key = '11111111-1111-4111-8111-111111111111'
const order: PlacedOrder = { id: '1', orderNumber: 'PS' + 'a'.repeat(28), status: 'Placed', grandTotal: 1000000, currency: 'VND', paymentMethod: 'COD', paymentDueAt: null }
const quote: Quote = { items: [{ variantId: '2', quantity: 1, productName: 'Synthetic', productSlug: 'synthetic', sku: 'SYN', color: 'Black', storageGb: 128, ramGb: 8, available: 5, unitPrice: 1000000, unitDiscount: 0, lineTotal: 1000000, promotionName: null }], subtotal: 1000000, discountTotal: 0, shippingFee: 0, grandTotal: 1000000, currency: 'VND', quoteHash: 'a'.repeat(64) }
let committed = false
beforeEach(() => {
  committed = false; mockLocations(); localStorage.setItem(cartStorageKey, JSON.stringify([{ variantId: '2', quantity: 1 }]))
  vi.spyOn(quotes, 'get').mockResolvedValue(quote)
  vi.spyOn(orders, 'session').mockResolvedValue({ checkoutKey: key, expiresAt: '2026-10-09T10:30:00Z' })
  vi.spyOn(orders, 'result').mockImplementation(async () => ({ state: committed ? 'Completed' : 'Issued', expiresAt: '2026-10-09T10:30:00Z', order: committed ? order : null }))
  vi.spyOn(orders, 'place').mockImplementation(async () => { committed = true; return order })
})
afterEach(() => { cleanup(); vi.restoreAllMocks() })
function open() { render(<AuthContext.Provider value={{ status: 'anonymous', refresh: vi.fn(), clear: vi.fn() }}><CartProvider><MemoryRouter initialEntries={['/checkout']}><Routes><Route path="/checkout" element={<CheckoutPage />} /><Route path="/checkout/success" element={<CheckoutSuccessPage />} /></Routes></MemoryRouter></CartProvider></AuthContext.Provider>) }
async function fill() {
  await screen.findByLabelText('Họ tên người nhận'); fireEvent.change(screen.getByLabelText('Họ tên người nhận'), { target: { value: 'Synthetic name' } }); fireEvent.change(screen.getByLabelText('Số điện thoại'), { target: { value: '0000000000' } }); fireEvent.change(screen.getByLabelText('Email nhận đơn'), { target: { value: 'guest@example.invalid' } }); fireEvent.change(screen.getByLabelText('Số nhà, đường'), { target: { value: 'Synthetic street' } });
  await screen.findByRole('option', { name: 'Thành phố Hồ Chí Minh' }); fireEvent.change(screen.getByLabelText('Tỉnh/thành phố'), { target: { value: '79' } }); await screen.findByRole('option', { name: 'Phường Synthetic' }); fireEvent.change(screen.getByLabelText('Phường/xã'), { target: { value: '26734' } });
}
async function price() { fireEvent.click(screen.getByRole('button', { name: 'Tính báo giá' })); await screen.findByLabelText('Tôi xác nhận sản phẩm, số lượng và tổng tiền trên.'); fireEvent.click(screen.getByLabelText('Tôi xác nhận sản phẩm, số lượng và tổng tiền trên.')) }
test('requires valid contact/address and explicit quote confirmation; consent is unchecked', async () => {
  open(); await screen.findByLabelText('Email nhận đơn'); expect((screen.getByRole('checkbox') as HTMLInputElement).checked).toBe(false); expect((screen.getByRole('button', { name: 'Đặt hàng' }) as HTMLButtonElement).disabled).toBe(true)
  fireEvent.click(screen.getByRole('button', { name: 'Tính báo giá' })); await screen.findByText('Nhập họ tên người nhận.'); expect(orders.session).not.toHaveBeenCalled(); expect(quotes.get).not.toHaveBeenCalled()
  await fill(); await price(); fireEvent.click(screen.getByRole('button', { name: 'Đặt hàng' })); await screen.findByRole('heading', { name: 'Đặt hàng thành công' }); expect(orders.place).toHaveBeenCalledWith(key, expect.objectContaining({ email: 'guest@example.invalid', createAccountConsent: false, countryCode: 'VN', quoteHash: quote.quoteHash })); expect(JSON.parse(localStorage.getItem(cartStorageKey)!)).toEqual([])
})
test('lost response retries exact payload/key and never persists PII', async () => {
  vi.mocked(orders.place).mockRejectedValueOnce(new ApiError(0, 'TIMEOUT')); open(); await fill(); await price(); fireEvent.click(screen.getByRole('button', { name: 'Đặt hàng' })); await screen.findByRole('button', { name: 'Thử lại yêu cầu đặt hàng' });
  expect(screen.getByLabelText('Email nhận đơn').matches(':disabled')).toBe(true)
  const stored = sessionStorage.getItem(pendingCheckoutStorageKey)!; expect(stored).not.toContain('guest@example.invalid'); expect(stored).not.toContain('Synthetic street'); expect(Object.keys(JSON.parse(stored))).toEqual(['key', 'items'])
  fireEvent.click(screen.getByRole('button', { name: 'Thử lại yêu cầu đặt hàng' })); await screen.findByRole('heading', { name: 'Đặt hàng thành công' }); expect(vi.mocked(orders.place).mock.calls[1]).toEqual(vi.mocked(orders.place).mock.calls[0]); expect(orders.session).toHaveBeenCalledTimes(1)
})
test('PRICE_CHANGED fetches fresh quote and requires a new confirmation before retry', async () => {
  vi.mocked(orders.place).mockRejectedValueOnce(new ApiError(409, 'PRICE_CHANGED')); open(); await fill(); await price(); fireEvent.click(screen.getByRole('button', { name: 'Đặt hàng' })); await screen.findByText('Giá hoặc phí đã thay đổi. Vui lòng xem và xác nhận báo giá mới.')
  expect(screen.queryByLabelText('Tôi xác nhận sản phẩm, số lượng và tổng tiền trên.')).toBeNull(); expect((screen.getByRole('button', { name: 'Đặt hàng' }) as HTMLButtonElement).disabled).toBe(true)
  fireEvent.click(screen.getByRole('button', { name: 'Tính báo giá' })); await screen.findByText('Giá hoặc phí đã thay đổi. Xem và xác nhận báo giá mới.'); expect((screen.getByLabelText('Tôi xác nhận sản phẩm, số lượng và tổng tiền trên.') as HTMLInputElement).checked).toBe(false); expect(orders.place).toHaveBeenCalledTimes(1)
  fireEvent.click(screen.getByLabelText('Tôi xác nhận sản phẩm, số lượng và tổng tiền trên.')); fireEvent.click(screen.getByRole('button', { name: 'Đặt hàng' })); await screen.findByRole('heading', { name: 'Đặt hàng thành công' }); expect(orders.session).toHaveBeenCalledTimes(1)
})
test('reload recovers committed order from server with no new placement', async () => {
  committed = true; savePendingCheckout({ key, items: [{ variantId: '2', quantity: 1 }] }); open(); await screen.findByRole('heading', { name: 'Đặt hàng thành công' }); expect(orders.place).not.toHaveBeenCalled(); expect(orders.session).not.toHaveBeenCalled(); expect(localStorage.getItem(cartStorageKey)).toBe('[]')
})
test('double submit while pending sends once and another-tab cart edit is not cleared', async () => {
  let resolve!: (v: PlacedOrder) => void; vi.mocked(orders.place).mockImplementation(() => new Promise(r => { resolve = r })); open(); await fill(); await price(); const button = screen.getByRole('button', { name: 'Đặt hàng' }); fireEvent.click(button); fireEvent.click(button); await waitFor(() => expect(orders.place).toHaveBeenCalledTimes(1));
  localStorage.setItem(cartStorageKey, JSON.stringify([{ variantId: '3', quantity: 2 }])); window.dispatchEvent(new StorageEvent('storage', { key: cartStorageKey })); committed = true; resolve(order); await screen.findByRole('heading', { name: 'Đặt hàng thành công' }); expect(JSON.parse(localStorage.getItem(cartStorageKey)!)).toEqual([{ variantId: '3', quantity: 2 }])
})
