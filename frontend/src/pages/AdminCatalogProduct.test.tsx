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
  sessionStorage.clear()
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
  expect((screen.getByLabelText('File ảnh') as HTMLInputElement).matches(':disabled')).toBe(true)
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

test('JPEG upload uses multipart and exposes a managed download link after success', async () => {
  const image = { id: '9', variantId: null, altText: 'JPEG phone', sortOrder: 0, imageUrl: '/api/v1/catalog-images/cloud-' + 'a'.repeat(32) + '.png' }
  const upload = vi.spyOn(catalog, 'upload').mockResolvedValue(image)
  await open(); fireEvent.change(screen.getByLabelText('File ảnh'), { target: { files: [new File(['synthetic'], 'phone.jpg', { type: 'image/jpeg' })] } });
  fireEvent.change(screen.getByLabelText('Mô tả ảnh'), { target: { value: image.altText } }); fireEvent.submit(screen.getByRole('button', { name: 'Tải ảnh lên' }).closest('form')!);
  const link = await screen.findByRole('link', { name: 'Tải ảnh JPEG phone' }); expect(link.getAttribute('href')).toBe(image.imageUrl + '?download=true')
  expect((upload.mock.calls[0][1].get('file') as File).name).toBe('phone.jpg')
})
test('server field validation is associated with the matching input', async () => {
  vi.spyOn(catalog, 'saveProduct').mockRejectedValue(new ApiError(400, 'VALIDATION_ERROR', 'synthetic-trace', { Name: ['Tên không hợp lệ.'] }))
  await open(); fireEvent.click(screen.getByRole('button', { name: 'Lưu sản phẩm' }))
  const input = screen.getByLabelText('Tên sản phẩm')
  await waitFor(() => expect(input.getAttribute('aria-invalid')).toBe('true'))
  const description = input.getAttribute('aria-describedby')!
  expect(document.getElementById(description)?.textContent).toBe('Tên không hợp lệ.')
})

test('lost upload response retries the same key and frozen metadata, then clears pending state', async () => {
  const image = { id: '9', variantId: null, altText: 'Retry phone', sortOrder: 0, imageUrl: '/api/v1/catalog-images/cloud-' + 'a'.repeat(32) + '.png' }
  const upload = vi.spyOn(catalog, 'upload').mockRejectedValueOnce(new ApiError(0, 'NETWORK_ERROR')).mockResolvedValueOnce(image)
  await open(); fireEvent.change(screen.getByLabelText('File ảnh'), { target: { files: [new File(['synthetic'], 'phone.jpg', { type: 'image/jpeg' })] } })
  fireEvent.change(screen.getByLabelText('Mô tả ảnh'), { target: { value: image.altText } }); fireEvent.submit(screen.getByRole('button', { name: 'Tải ảnh lên' }).closest('form')!)
  await screen.findByRole('alert'); expect(screen.getByLabelText('Mô tả ảnh').matches(':disabled')).toBe(true)
  const first = upload.mock.calls[0][1]; expect(first.get('operationKey')).toBeTruthy()
  fireEvent.submit(screen.getByRole('button', { name: 'Thử lại tải ảnh' }).closest('form')!)
  await screen.findByRole('link', { name: 'Tải ảnh Retry phone' })
  expect(upload.mock.calls[1][1].get('operationKey')).toBe(first.get('operationKey')); expect(upload.mock.calls[1][1].get('file')).toBe(first.get('file'))
  expect(sessionStorage.getItem('phonestore.catalog-upload.2')).toBeNull(); expect(screen.getByLabelText('Mô tả ảnh').matches(':disabled')).toBe(false)
})

test('reload recovers a committed upload without uploading another file or duplicating its row', async () => {
  const key = crypto.randomUUID(); const image = { id: '9', variantId: null, altText: 'Recovered phone', sortOrder: 0, imageUrl: '/api/v1/catalog-images/cloud-' + 'b'.repeat(32) + '.png' }
  sessionStorage.setItem('phonestore.catalog-upload.2', JSON.stringify({ key, variantId: '', altText: image.altText, sortOrder: '0' }))
  vi.mocked(catalog.product).mockResolvedValue({ ...fixture, images: [image] })
  const recover = vi.spyOn(catalog, 'uploadResult').mockResolvedValue(image); const upload = vi.spyOn(catalog, 'upload')
  await open(); fireEvent.click(screen.getByRole('button', { name: 'Kiểm tra kết quả tải ảnh' }))
  await screen.findByText('Đã tìm thấy ảnh đã tải lên.'); expect(recover.mock.calls[0].slice(0, 2)).toEqual(['2', key]); expect(upload).not.toHaveBeenCalled()
  expect(screen.getAllByRole('link', { name: 'Tải ảnh Recovered phone' })).toHaveLength(1); expect(sessionStorage.getItem('phonestore.catalog-upload.2')).toBeNull()
})
