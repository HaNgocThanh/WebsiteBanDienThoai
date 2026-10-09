import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { MemoryRouter } from 'react-router'
import { CartProvider } from '../cart/CartProvider'
import { cartStorageKey } from '../cart/CartContext'
import { quotes } from '../services/quotes'
import type { Quote } from '../services/quotes'
import { ApiError } from '../services/errors'
import { CartPage } from './CartPage'
import { mockLocations } from '../test/locationFixtures'
import { cartDisplay } from '../services/cartDisplay'

const quote: Quote = { items: [{ variantId: '2', quantity: 1, productName: 'Synthetic', productSlug: 'synthetic', sku: 'SYNTHETIC', color: 'Black', storageGb: 128, ramGb: 8, available: 5, unitPrice: 1000000, unitDiscount: 0, lineTotal: 1000000, promotionName: null }], subtotal: 1000000, discountTotal: 0, shippingFee: 0, grandTotal: 1000000, currency: 'VND', quoteHash: 'a'.repeat(64) }
const displayVariant = { variantId: '2', productName: 'Synthetic', productSlug: 'synthetic', sku: 'SYNTHETIC', color: 'Black', storageGb: 128, ramGb: 8, imageUrl: '/api/v1/catalog-images/' + 'a'.repeat(32) + '.png', imageAltText: 'Synthetic Black' }
beforeEach(() => { mockLocations(); vi.spyOn(cartDisplay, 'get').mockResolvedValue([displayVariant]); window.localStorage.setItem(cartStorageKey, JSON.stringify([{ variantId: '2', quantity: 1, unitPrice: 0 }])) })
afterEach(() => { cleanup(); window.localStorage.clear(); vi.restoreAllMocks() })
function open() { render(<CartProvider><MemoryRouter><CartPage /></MemoryRouter></CartProvider>) }
async function destination() {
  fireEvent.change(screen.getByLabelText('Số nhà, đường'), { target: { value: 'Synthetic street' } })
  await screen.findByRole('option', { name: 'Thành phố Hồ Chí Minh' })
  fireEvent.change(screen.getByLabelText('Tỉnh/thành phố'), { target: { value: '79' } })
  await screen.findByRole('option', { name: 'Phường Synthetic' })
  fireEvent.change(screen.getByLabelText('Phường/xã'), { target: { value: '26734' } })
}
async function calculate() { await destination(); fireEvent.click(screen.getByRole('button', { name: 'Tính lại báo giá' })); await screen.findByText('Báo giá hiện tại') }
test('price conflict refreshes quote but requires explicit re-confirmation', async () => {
  const get = vi.spyOn(quotes, 'get').mockResolvedValueOnce(quote).mockRejectedValueOnce(new ApiError(409, 'PRICE_CHANGED')).mockResolvedValueOnce({ ...quote, quoteHash: 'b'.repeat(64), items: [{ ...quote.items[0], unitPrice: 1200000, lineTotal: 1200000 }], subtotal: 1200000, grandTotal: 1200000 })
  open(); await calculate(); fireEvent.click(screen.getByRole('button', { name: 'Xác nhận báo giá hiện tại' })); expect(screen.getByRole('status').textContent).toContain('đã xác nhận')
  fireEvent.click(screen.getByRole('button', { name: 'Tính lại báo giá' })); await screen.findByText('Giá hoặc phí đã thay đổi. Xem báo giá mới và xác nhận lại.')
  expect(screen.queryByRole('status')).toBeNull(); expect(get.mock.calls[1][2]).toBe(quote.quoteHash); expect(get.mock.calls[2][2]).toBeUndefined()
  fireEvent.click(screen.getByRole('button', { name: 'Xác nhận báo giá hiện tại' })); expect(screen.getByRole('status').textContent).toContain('đã xác nhận')
})
test('editing quantity invalidates quote immediately and sends only valid committed cart', async () => {
  const get = vi.spyOn(quotes, 'get').mockResolvedValue(quote); open(); await calculate()
  fireEvent.change(screen.getByRole('spinbutton'), { target: { value: '0' } }); expect(screen.queryByText('Báo giá hiện tại')).toBeNull()
  fireEvent.submit(screen.getByRole('spinbutton').closest('form')!); expect(screen.getByRole('alert').textContent).toContain('số nguyên'); expect(get).toHaveBeenCalledTimes(1)
  fireEvent.change(screen.getByRole('spinbutton'), { target: { value: '3' } }); fireEvent.submit(screen.getByRole('spinbutton').closest('form')!)
  expect(JSON.parse(window.localStorage.getItem(cartStorageKey)!)).toEqual([{ variantId: '2', quantity: 3 }])
})
test('late response after destination changes cannot confirm an old quote', async () => {
  let resolve!: (value: Quote) => void
  vi.spyOn(quotes, 'get').mockImplementation(() => new Promise(r => { resolve = r })); open()
  await destination(); fireEvent.click(screen.getByRole('button', { name: 'Tính lại báo giá' }))
  fireEvent.change(screen.getByLabelText('Tỉnh/thành phố'), { target: { value: '48' } }); resolve(quote)
  await waitFor(() => expect(screen.queryByText('Báo giá hiện tại')).toBeNull())
})
test('corrupt storage and network error offer recovery without false quote confirmation', async () => {
  window.localStorage.setItem(cartStorageKey, '{broken'); open(); fireEvent.click(screen.getByRole('button', { name: 'Xóa giỏ để khôi phục' })); expect(window.localStorage.getItem(cartStorageKey)).toBe('[]'); cleanup()
  window.localStorage.setItem(cartStorageKey, JSON.stringify([{ variantId: '2', quantity: 1 }]))
  vi.spyOn(quotes, 'get').mockRejectedValue(new ApiError(0, 'NETWORK_ERROR')); open()
  await destination(); fireEvent.click(screen.getByRole('button', { name: 'Tính lại báo giá' }))
  await screen.findByRole('alert'); expect(screen.queryByText('Báo giá hiện tại')).toBeNull(); expect(screen.queryByRole('button', { name: 'Xác nhận báo giá hiện tại' })).toBeNull()
})
test('name, selected variant and image load before quote, survive quantity edits, and icon removes the row', async () => {
  open(); await screen.findByRole('heading', { name: 'Synthetic' })
  expect(screen.getByText('Black · 128 GB · RAM 8 GB')).toBeTruthy()
  const image = screen.getByRole('img', { name: 'Synthetic Black' })
  expect(image.getAttribute('src')).toBe(displayVariant.imageUrl)
  expect(screen.queryByText('Phiên bản #2')).toBeNull(); expect(screen.queryByText('Báo giá hiện tại')).toBeNull()
  fireEvent.change(screen.getByRole('spinbutton'), { target: { value: '2' } })
  expect(screen.getByRole('heading', { name: 'Synthetic' })).toBeTruthy()
  fireEvent.error(image); expect(screen.getByRole('img', { name: 'Chưa có ảnh: Synthetic Black' })).toBeTruthy()
  const remove = screen.getByRole('button', { name: 'Xóa Synthetic, Black · 128 GB · RAM 8 GB' })
  expect(remove.textContent).toBe(''); expect(remove.className).toBe('cart-remove')
  fireEvent.click(remove); expect(screen.getByRole('heading', { name: 'Giỏ hàng đang trống' })).toBeTruthy(); expect(window.localStorage.getItem(cartStorageKey)).toBe('[]')
})
test('missing public variant is removable; display failure has a retry', async () => {
  vi.mocked(cartDisplay.get).mockRejectedValueOnce(new ApiError(0, 'NETWORK_ERROR')).mockResolvedValueOnce([])
  open(); await screen.findByRole('button', { name: 'Tải lại thông tin sản phẩm' })
  fireEvent.click(screen.getByRole('button', { name: 'Tải lại thông tin sản phẩm' }))
  await screen.findByRole('heading', { name: 'Sản phẩm không còn được bán' })
  fireEvent.click(screen.getByRole('button', { name: 'Xóa Sản phẩm không còn được bán' }))
  expect(window.localStorage.getItem(cartStorageKey)).toBe('[]')
})
