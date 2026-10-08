import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { catalog } from '../services/catalog'
import type { Product } from '../services/catalog'
import { ApiError } from '../services/errors'
import { AdminProductPage } from './AdminCatalogProduct'

// Synthetic unit fixtures only; SQL/browser acceptance is in e2e/catalog.spec.ts.
const fixture: Product = { id: '2', name: 'Test phone', slug: 'test-phone', brandId: '1', categoryId: '1', description: '', specificationsJson: null, isActive: true, version: 'AAAAAAAAAAE=', variants: [], images: [] }
beforeEach(() => {
  vi.spyOn(catalog, 'product').mockResolvedValue(fixture)
  vi.spyOn(catalog, 'lookups').mockResolvedValue([{ id: '1', name: 'Test parent', slug: 'test-parent', isActive: true }])
})
afterEach(() => { cleanup(); vi.restoreAllMocks() })
async function open() { render(<MemoryRouter initialEntries={['/admin/products/2']}><Routes><Route path="/admin/products/:id" element={<AdminProductPage />} /></Routes></MemoryRouter>); await screen.findByLabelText('Tên sản phẩm') }
test.each([new ApiError(409, 'DUPLICATE_CATALOG'), new ApiError(0, 'TIMEOUT')])('failed save keeps draft and never reports success: %s', async failure => {
  const save = vi.spyOn(catalog, 'saveProduct').mockRejectedValue(failure)
  await open(); fireEvent.change(screen.getByLabelText('Tên sản phẩm'), { target: { value: 'Draft phone' } }); fireEvent.click(screen.getByRole('button', { name: 'Lưu sản phẩm' }))
  expect((await screen.findByRole('alert')).textContent).toContain(failure.message)
  expect(screen.queryByText('Đã lưu sản phẩm.')).toBeNull()
  expect((screen.getByLabelText('Tên sản phẩm') as HTMLInputElement).value).toBe('Draft phone')
  expect(save).toHaveBeenCalledTimes(1)
})
test('stale edit uses loaded ETag and reloads only after explicit refresh', async () => {
  const save = vi.spyOn(catalog, 'saveProduct').mockRejectedValue(new ApiError(412, 'VERSION_MISMATCH'))
  await open(); fireEvent.click(screen.getByRole('button', { name: 'Lưu sản phẩm' }))
  await screen.findByRole('alert')
  expect(save.mock.calls[0][1]?.version).toBe(fixture.version)
  expect(catalog.product).toHaveBeenCalledTimes(1)
  vi.mocked(catalog.product).mockResolvedValue({ ...fixture, name: 'Latest phone', version: 'AAAAAAAAAAI=' })
  fireEvent.click(screen.getByRole('button', { name: 'Tải phiên bản mới (bỏ thay đổi chưa lưu)' }))
  await waitFor(() => expect((screen.getByLabelText('Tên sản phẩm') as HTMLInputElement).value).toBe('Latest phone'))
  expect(save).toHaveBeenCalledTimes(1)
})
test('pending save locks product, variant and image controls and double click sends once', async () => {
  let complete!: (product: Product) => void
  const save = vi.spyOn(catalog, 'saveProduct').mockImplementation(() => new Promise(resolve => { complete = resolve }))
  await open(); const button = screen.getByRole('button', { name: 'Lưu sản phẩm' })
  fireEvent.click(button); fireEvent.click(button)
  expect(save).toHaveBeenCalledTimes(1)
  expect((screen.getByLabelText('SKU') as HTMLInputElement).matches(':disabled')).toBe(true)
  expect((screen.getByLabelText('File PNG') as HTMLInputElement).matches(':disabled')).toBe(true)
  complete(fixture)
  await screen.findByText('Đã lưu sản phẩm.')
  expect((screen.getByLabelText('SKU') as HTMLInputElement).matches(':disabled')).toBe(false)
})
test('invalid specifications stop mutation and expose correction', async () => {
  const save = vi.spyOn(catalog, 'saveProduct')
  await open(); fireEvent.change(screen.getByLabelText('Thông số (JSON)'), { target: { value: '[]' } }); fireEvent.click(screen.getByRole('button', { name: 'Lưu sản phẩm' }))
  expect(screen.getByRole('alert').textContent).toContain('đối tượng JSON hợp lệ')
  expect(save).not.toHaveBeenCalled()
})
test('server field validation is associated with the matching input', async () => {
  vi.spyOn(catalog, 'saveProduct').mockRejectedValue(new ApiError(400, 'VALIDATION_ERROR', 'synthetic-trace', { Name: ['Tên không hợp lệ.'] }))
  await open(); fireEvent.click(screen.getByRole('button', { name: 'Lưu sản phẩm' }))
  const input = screen.getByLabelText('Tên sản phẩm')
  await waitFor(() => expect(input.getAttribute('aria-invalid')).toBe('true'))
  const description = input.getAttribute('aria-describedby')!
  expect(document.getElementById(description)?.textContent).toBe('Tên không hợp lệ.')
})
