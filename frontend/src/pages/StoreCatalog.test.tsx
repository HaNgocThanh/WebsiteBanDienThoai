import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { storeCatalog } from '../services/storeCatalog'
import { ApiError } from '../services/errors'
import { StoreProductPage, StoreProductsPage } from './StoreCatalog'
import { CartProvider } from '../cart/CartProvider'

const lookup = { id: '1', name: 'Synthetic', slug: 'synthetic', isActive: true }
beforeEach(() => { vi.spyOn(storeCatalog, 'brands').mockResolvedValue([lookup]); vi.spyOn(storeCatalog, 'categories').mockResolvedValue([lookup]) })
afterEach(() => { cleanup(); vi.restoreAllMocks() })
function openList() { render(<MemoryRouter initialEntries={['/products?search=Test&page=3']}><Routes><Route path="/products" element={<StoreProductsPage />} /></Routes></MemoryRouter>) }
test('combined filters reset page and invalid price range never sends a request', async () => {
  const products = vi.spyOn(storeCatalog, 'products').mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0 })
  openList(); await screen.findByText('Không tìm thấy sản phẩm phù hợp')
  fireEvent.change(screen.getByLabelText('Giá tối thiểu (VND)'), { target: { value: '20' } }); fireEvent.change(screen.getByLabelText('Giá tối đa (VND)'), { target: { value: '10' } }); fireEvent.click(screen.getByRole('button', { name: 'Áp dụng bộ lọc' }))
  expect(screen.getByText('Giá tối đa phải lớn hơn hoặc bằng giá tối thiểu.')).toBeTruthy(); expect(products).toHaveBeenCalledTimes(1)
  fireEvent.change(screen.getByLabelText('Giá tối đa (VND)'), { target: { value: '30' } }); fireEvent.change(screen.getByLabelText('Hãng'), { target: { value: '1' } }); fireEvent.click(screen.getByRole('button', { name: 'Áp dụng bộ lọc' }))
  await waitFor(() => expect(products).toHaveBeenCalledTimes(2))
  const query = products.mock.calls[1][0]; expect(query.get('page')).toBeNull(); expect(query.get('brandId')).toBe('1'); expect(query.get('minPrice')).toBe('20'); expect(query.get('search')).toBe('Test')
})
test('failed public load can retry without losing URL filters', async () => {
  const products = vi.spyOn(storeCatalog, 'products').mockRejectedValueOnce(new ApiError(0, 'NETWORK_ERROR')).mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, totalCount: 0 })
  openList(); fireEvent.click(await screen.findByRole('button', { name: 'Thử tải lại' })); await screen.findByText('Không tìm thấy sản phẩm phù hợp')
  expect(products.mock.calls[1][0].toString()).toBe(products.mock.calls[0][0].toString())
})
test('variant selection changes actual price, availability and only matching images; description stays text', async () => {
  const imageUrl = '/api/v1/catalog-images/' + 'a'.repeat(32) + '.png'
  vi.spyOn(storeCatalog, 'product').mockResolvedValue({ id: '1', name: 'Synthetic phone', slug: 'test', description: '<script>alert(1)</script>', specificationsJson: null, brand: lookup, category: lookup, variants: [
    { id: '1', sku: 'A', color: 'Black', storageGb: 128, ramGb: 8, price: 1000000, available: 3 },
    { id: '2', sku: 'B', color: 'White', storageGb: 256, ramGb: 8, price: 2000000, available: 0 },
  ], images: [{ id: '1', variantId: '2', sortOrder: 0, altText: 'White image', imageUrl }] })
  const view = render(<CartProvider><MemoryRouter initialEntries={['/products/test']}><Routes><Route path="/products/:slug" element={<StoreProductPage />} /></Routes></MemoryRouter></CartProvider>)
  await screen.findByText('Chọn phiên bản để xem giá'); expect(screen.queryAllByRole('radio', { checked: true })).toHaveLength(0); expect(screen.getByRole('button', { name: 'Thêm vào giỏ hàng' }).matches(':disabled')).toBe(true); expect(screen.queryByAltText('White image')).toBeNull(); expect(view.container.querySelector('script')).toBeNull()
  fireEvent.click(screen.getByRole('radio', { name: 'White · 256 GB · RAM 8 GB' })); expect(screen.getByText('Hết hàng')).toBeTruthy(); expect(screen.getByText('2.000.000 ₫')).toBeTruthy(); expect(screen.getByAltText('White image')).toBeTruthy()
})
