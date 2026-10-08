import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AuthContext } from '../auth/AuthContext'
import type { Session } from '../services/auth'
const adminSessionFixture: Session = { userId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', email: 'synthetic@example.invalid', fullName: 'Synthetic Admin', phone: null, emailConfirmed: true, roles: ['Admin'] }
import { ApiError } from '../services/errors'
import { inventoryApi, pendingKey } from '../services/inventory'
import type { StockResult } from '../services/inventory'
import { AdminInventoryDetailPage } from './AdminInventory'

// Synthetic unit fixtures only; SQL idempotency and browser acceptance are separate suites.
const balance = { variantId: '2', productId: '1', productName: 'Test phone', sku: 'TEST', color: 'Black', storageGb: 128, ramGb: 8, isActive: true, onHand: 0, reserved: 0, available: 0, version: 'AAAAAAAAAAE=' }
const result: StockResult = { inventory: { ...balance, onHand: 10, available: 10 }, movement: { id: '1', variantId: '2', kind: 'Receive', onHandDelta: 10, reservedDelta: 0, reason: 'Test receive', actorUserId: null, createdAt: '2026-10-08T00:00:00Z', operationKey: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' }, isReplay: true }
beforeEach(() => { sessionStorage.clear(); vi.spyOn(inventoryApi, 'detail').mockResolvedValue(balance); vi.spyOn(inventoryApi, 'movements').mockResolvedValue({ items: [], page: 1, pageSize: 10, totalCount: 0 }) })
afterEach(() => { cleanup(); vi.restoreAllMocks(); sessionStorage.clear() })
async function open() { render(<AuthContext.Provider value={{ status: 'authenticated', user: adminSessionFixture, refresh: vi.fn(), clear: vi.fn() }}><MemoryRouter initialEntries={['/admin/inventory/2']}><Routes><Route path="/admin/inventory/:variantId" element={<AdminInventoryDetailPage />} /></Routes></MemoryRouter></AuthContext.Provider>); await screen.findByLabelText('Lý do') }
async function submit() { fireEvent.change(screen.getByLabelText('Số lượng thay đổi'), { target: { value: '10' } }); fireEvent.change(screen.getByLabelText('Lý do'), { target: { value: 'Test receive' } }); fireEvent.click(screen.getByRole('button', { name: 'Ghi thao tác kho' })) }
test('timeout retains immutable payload/key and reload retries that same operation only', async () => {
  const write = vi.spyOn(inventoryApi, 'write').mockRejectedValueOnce(new ApiError(0, 'TIMEOUT')).mockResolvedValueOnce(result)
  await open(); await submit(); await screen.findByRole('alert')
  const original = write.mock.calls[0][0]
  expect(sessionStorage.getItem(pendingKey(adminSessionFixture.userId, '2'))).toContain(original.operationKey)
  expect(screen.queryByText('Thao tác kho đã được xác nhận.', { exact: false })).toBeNull()
  cleanup()
  render(<AuthContext.Provider value={{ status: 'authenticated', user: adminSessionFixture, refresh: vi.fn(), clear: vi.fn() }}><MemoryRouter initialEntries={['/admin/inventory/2']}><Routes><Route path="/admin/inventory/:variantId" element={<AdminInventoryDetailPage />} /></Routes></MemoryRouter></AuthContext.Provider>)
  fireEvent.click(await screen.findByRole('button', { name: 'Thử lại cùng thao tác' }))
  await screen.findByText(/Thao tác kho đã được xác nhận/)
  expect(write.mock.calls[1][0]).toEqual(original)
  expect(sessionStorage.getItem(pendingKey(adminSessionFixture.userId, '2'))).toBeNull()
})
test('double click sends once and pending cannot be edited or silently replaced', async () => {
  let complete!: (value: StockResult) => void
  const write = vi.spyOn(inventoryApi, 'write').mockImplementation(() => new Promise(resolve => { complete = resolve }))
  await open(); await submit(); fireEvent.click(screen.getByRole('button', { name: 'Thử lại cùng thao tác' }))
  expect(write).toHaveBeenCalledTimes(1); expect(screen.queryByLabelText('Số lượng thay đổi')).toBeNull()
  complete(result); await screen.findByText(/Thao tác kho đã được xác nhận/)
})
test('zero or unsafe quantity stops before storing or sending', async () => {
  const write = vi.spyOn(inventoryApi, 'write')
  await open(); fireEvent.change(screen.getByLabelText('Số lượng thay đổi'), { target: { value: '2147483648' } }); fireEvent.change(screen.getByLabelText('Lý do'), { target: { value: 'Test receive' } }); fireEvent.click(screen.getByRole('button', { name: 'Ghi thao tác kho' }))
  expect(screen.getByRole('alert').textContent).toContain('số lượng nguyên'); expect(write).not.toHaveBeenCalled(); expect(sessionStorage.length).toBe(0)
})
test('insufficient stock never reports success and discard requires explicit history confirmation', async () => {
  vi.spyOn(inventoryApi, 'write').mockRejectedValue(new ApiError(409, 'INSUFFICIENT_AVAILABLE'))
  await open(); await submit(); await screen.findByRole('alert')
  expect(screen.queryByText(/Thao tác kho đã được xác nhận/)).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Bỏ thao tác sau khi kiểm tra' }))
  expect(sessionStorage.length).toBe(1)
  fireEvent.click(screen.getByRole('button', { name: 'Xác nhận đã kiểm tra lịch sử' }))
  await waitFor(() => expect(screen.getByLabelText('Số lượng thay đổi')).toBeTruthy()); expect(sessionStorage.length).toBe(0)
})
