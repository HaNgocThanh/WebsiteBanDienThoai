import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, test, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AuthContext } from '../auth/AuthContext'
import { orderManagement } from '../services/orderManagement'
import type { OrderDetail } from '../services/orderManagement'
import { ApiError } from '../services/errors'
import { OrderDetailPage, OrderListPage, NotificationsPage } from './OrderManagement'
const user = { userId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', email: 'synthetic@example.invalid', fullName: 'Synthetic', phone: null, emailConfirmed: true, roles: ['Customer', 'Admin'] }
const detail: OrderDetail = { version: 'AAAAAAAAAAE=', allowedActions: [], paymentSummary: { confirmed: 0, remaining: 100, refunded: 0, refundPending: 0 }, snapshot: { id: '1', orderNumber: 'PS' + 'a'.repeat(28), status: 'Placed', paymentMethod: 'COD', currency: 'VND', grandTotal: 100, paymentDueAt: null, subtotal: 100, discountTotal: 0, shippingFee: 0, recipientName: 'Snapshot recipient', phone: '0000000000', addressLine: 'Snapshot street', locality: null, province: 'HCM', countryCode: 'VN', note: null, createdAt: '2026-10-09T00:00:00Z', createAccountConsent: false, items: [{ id: '1', variantId: '2', productName: 'Snapshot phone', sku: 'SYN', variant: 'Black', quantity: 1, unitPrice: 100, unitDiscount: 0, lineTotal: 100 }], timeline: [{ fromStatus: null, toStatus: 'Placed', createdAt: '2026-10-09T00:00:00Z' }] }, customerId: user.userId, customerEmail: user.email, notes: [{ id: '1', actorUserId: user.userId, text: 'PRIVATE SYNTHETIC', createdAt: '2026-10-09T00:00:00Z' }], history: [], payments: [], refunds: [], reservations: [] }
afterEach(() => { cleanup(); vi.restoreAllMocks() })
function open(element: React.ReactNode, entry = '/orders') { render(<AuthContext.Provider value={{ status: 'authenticated', user, refresh: vi.fn(), clear: vi.fn() }}><MemoryRouter initialEntries={[entry]}><Routes><Route path="/orders" element={element} /><Route path="/orders/:id" element={element} /></Routes></MemoryRouter></AuthContext.Provider>) }
test('list preserves URL filtering/paging and exposes empty/retry states', async () => {
  const list = vi.spyOn(orderManagement, 'list').mockRejectedValueOnce(new ApiError(0, 'NETWORK_ERROR')).mockResolvedValue({ items: [], page: 2, pageSize: 1, totalCount: 2 })
  open(<OrderListPage />, '/orders?fromDate=2026-10-09&toDate=2026-10-09&page=2&pageSize=1'); fireEvent.click(await screen.findByRole('button', { name: 'Thử tải lại' })); await screen.findByText('Không có đơn hàng phù hợp.')
  expect(list.mock.calls[1][0].get('page')).toBe('2'); fireEvent.click(screen.getByRole('button', { name: 'Trang trước' })); await waitFor(() => expect(list.mock.calls.at(-1)?.[0].get('page')).toBe('1')); expect(list.mock.calls.at(-1)?.[0].get('fromDate')).toBe('2026-10-09')
})
test('customer detail renders snapshots but never admin notes/history even if extra fields arrive', async () => {
  vi.spyOn(orderManagement, 'detail').mockResolvedValue(detail); open(<OrderDetailPage />, '/orders/1'); await screen.findByText('Snapshot phone'); expect(screen.getByText('Snapshot street, HCM, Việt Nam')).toBeTruthy(); expect(screen.queryByText('PRIVATE SYNTHETIC')).toBeNull(); expect(screen.queryByLabelText('Nội dung ghi chú nội bộ')).toBeNull()
})
test('Admin note timeout freezes payload and retries same operation without a second click mutation', async () => {
  vi.spyOn(orderManagement, 'detail').mockResolvedValue(detail); const note = vi.spyOn(orderManagement, 'note').mockRejectedValueOnce(new ApiError(0, 'TIMEOUT')).mockResolvedValue({ id: '2', actorUserId: user.userId, text: 'Synthetic new', createdAt: '2026-10-09T00:00:00Z' })
  open(<OrderDetailPage admin />, '/orders/1'); fireEvent.change(await screen.findByLabelText('Nội dung ghi chú nội bộ'), { target: { value: 'Synthetic new' } }); const submit = screen.getByRole('button', { name: 'Lưu ghi chú' }); fireEvent.click(submit); fireEvent.click(submit); await screen.findByRole('alert'); expect(note).toHaveBeenCalledTimes(1); expect((screen.getByLabelText('Nội dung ghi chú nội bộ').closest('fieldset') as HTMLFieldSetElement).disabled).toBe(true)
  fireEvent.click(screen.getByRole('button', { name: 'Thử lưu lại ghi chú' })); await waitFor(() => expect(note).toHaveBeenCalledTimes(2)); expect(note.mock.calls[1].slice(0, 3)).toEqual(note.mock.calls[0].slice(0, 3))
})
test('notification read only updates after server confirmation and failed request stays unread', async () => {
  const notices = vi.spyOn(orderManagement, 'notices').mockResolvedValue({ items: [{ id: '1', orderId: '2', type: 'Synthetic', title: 'Synthetic notification', body: 'Synthetic body', createdAt: '2026-10-09T00:00:00Z', readAt: null }], page: 1, pageSize: 20, totalCount: 1, unreadCount: 1 }); const read = vi.spyOn(orderManagement, 'read').mockRejectedValueOnce(new ApiError(0, 'NETWORK_ERROR')).mockResolvedValue(undefined)
  open(<NotificationsPage />); fireEvent.click(await screen.findByRole('button', { name: 'Đánh dấu đã đọc' })); await screen.findByRole('alert'); expect(screen.getByText('1 thông báo chưa đọc')).toBeTruthy(); fireEvent.click(screen.getByRole('button', { name: 'Đánh dấu đã đọc' })); await waitFor(() => expect(notices).toHaveBeenCalledTimes(2)); expect(read).toHaveBeenCalledTimes(2)
})
