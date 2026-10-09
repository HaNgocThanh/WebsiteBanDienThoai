import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { CartProvider } from '../cart/CartProvider'
import { cartStorageKey } from '../cart/CartContext'
import { quotes } from '../services/quotes'
import { ApiError } from '../services/errors'
import { CartPage } from './CartPage'
import { cartDisplay } from '../services/cartDisplay'

const displayVariant = { variantId: '2', productName: 'Synthetic', productSlug: 'synthetic', sku: 'SYNTHETIC', color: 'Black', storageGb: 128, ramGb: 8, imageUrl: '/api/v1/catalog-images/' + 'a'.repeat(32) + '.png', imageAltText: 'Synthetic Black' }
beforeEach(() => { vi.spyOn(cartDisplay, 'get').mockResolvedValue([displayVariant]); window.localStorage.setItem(cartStorageKey, JSON.stringify([{ variantId: '2', quantity: 1, unitPrice: 0 }])) })
afterEach(() => { cleanup(); window.localStorage.clear(); vi.restoreAllMocks() })
function open() { render(<CartProvider><MemoryRouter><Routes><Route path="/" element={<CartPage />} /><Route path="/checkout" element={<h1>Checkout destination</h1>} /></Routes></MemoryRouter></CartProvider>) }
test('cart opens checkout without requesting a shipping quote or showing address fields', async () => {
  const get = vi.spyOn(quotes, 'get'); open(); await screen.findByRole('heading', { name: 'Synthetic' })
  expect(screen.queryByRole('heading', { name: 'Giao hàng' })).toBeNull(); expect(screen.queryByLabelText('Số nhà, đường')).toBeNull()
  expect(get).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('link', { name: 'Tiến hành thanh toán' })); expect(screen.getByRole('heading', { name: 'Checkout destination' })).toBeTruthy()
})
test('quantity must be committed and valid before proceeding to checkout', async () => {
  open(); fireEvent.change(screen.getByRole('spinbutton'), { target: { value: '0' } }); expect(screen.queryByRole('link', { name: 'Tiến hành thanh toán' })).toBeNull()
  fireEvent.submit(screen.getByRole('spinbutton').closest('form')!); expect(screen.getByRole('alert').textContent).toContain('số nguyên')
  fireEvent.change(screen.getByRole('spinbutton'), { target: { value: '3' } }); fireEvent.submit(screen.getByRole('spinbutton').closest('form')!)
  expect(JSON.parse(window.localStorage.getItem(cartStorageKey)!)).toEqual([{ variantId: '2', quantity: 3 }]); expect(screen.getByRole('link', { name: 'Tiến hành thanh toán' })).toBeTruthy()
})
test('corrupt cart offers recovery instead of proceeding to checkout', () => {
  window.localStorage.setItem(cartStorageKey, '{broken'); open(); expect(screen.queryByRole('link', { name: 'Tiến hành thanh toán' })).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Xóa giỏ để khôi phục' })); expect(window.localStorage.getItem(cartStorageKey)).toBe('[]')
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
