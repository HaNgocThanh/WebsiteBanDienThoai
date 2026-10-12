import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, test, vi } from 'vitest'
import { OrderTransition } from './OrderTransition'
import { orderManagement, decodeOrderDetail } from '../services/orderManagement'
import type { OrderDetail } from '../services/orderManagement'
import { ApiError } from '../services/errors'
const detail = { version: 'AAAAAAAAAAE=', allowedActions: ['Shipping'] } as OrderDetail
const receipt = { orderId: '1', status: 'Shipping' as const, version: 'AAAAAAAAAAI=', historyId: '1', occurredAt: '2026-10-10T00:00:00Z' }
afterEach(() => { cleanup(); vi.restoreAllMocks() })
test('ambiguous transition retains exact version/key/reason and requires explicit confirmation', async () => {
  const transition = vi.spyOn(orderManagement, 'transition').mockRejectedValueOnce(new ApiError(0, 'TIMEOUT')).mockResolvedValue(receipt), reload = vi.fn()
  render(<OrderTransition id="1" detail={detail} reload={reload} />)
  fireEvent.change(screen.getByLabelText('Lý do cập nhật trạng thái'), { target: { value: 'Đã đóng gói và bàn giao cho đơn vị vận chuyển' } })
  expect(screen.getByRole('button', { name: /Chuyển sang/ }).matches(':disabled')).toBe(true)
  fireEvent.click(screen.getByRole('checkbox')); fireEvent.click(screen.getByRole('button', { name: /Chuyển sang/ }))
  const retry = await screen.findByRole('button', { name: 'Thử lại cùng bước xử lý' }); await waitFor(() => expect(retry.matches(':disabled')).toBe(false))
  expect(screen.getByLabelText('Lý do cập nhật trạng thái').matches(':disabled')).toBe(true)
  const first = transition.mock.calls[0][1]; fireEvent.click(retry)
  await waitFor(() => expect(reload).toHaveBeenCalledOnce()); expect(transition.mock.calls[1][1]).toEqual(first); expect(first.version).toBe(detail.version); expect(first.reason).toBe('Đã đóng gói và bàn giao cho đơn vị vận chuyển'); expect(screen.getByLabelText('Ghi chú bổ sung (không bắt buộc)').hasAttribute('required')).toBe(false)
})
test('stale version preserves error and requires explicit refresh without auto-transition', async () => {
  const transition = vi.spyOn(orderManagement, 'transition').mockRejectedValue(new ApiError(412, 'VERSION_CONFLICT')), reload = vi.fn()
  render(<OrderTransition id="1" detail={detail} reload={reload} />)
  fireEvent.change(screen.getByLabelText('Lý do cập nhật trạng thái'), { target: { value: 'Đã đóng gói và bàn giao cho đơn vị vận chuyển' } }); fireEvent.click(screen.getByRole('checkbox')); fireEvent.click(screen.getByRole('button', { name: /Chuyển sang/ }))
  const refresh = await screen.findByRole('button', { name: 'Tải lại trạng thái đơn' }); expect(reload).not.toHaveBeenCalled(); expect(transition).toHaveBeenCalledOnce(); expect(screen.getByRole('checkbox').matches(':checked')).toBe(false)
  expect(screen.getByRole('alert')).toBeTruthy(); fireEvent.click(refresh); expect(reload).toHaveBeenCalledOnce()
})
test('no allowed step provides no mutation button', () => {
  render(<OrderTransition id="1" detail={{ ...detail, allowedActions: [] }} reload={vi.fn()} />)
  expect(screen.queryByRole('button')).toBeNull()
})
test('decoder rejects unknown actions and actions leaked into a customer response', () => {
  const snapshot = { id: '1', orderNumber: 'PS' + 'a'.repeat(28), status: 'Placed', paymentMethod: 'COD', currency: 'VND', grandTotal: 1, paymentDueAt: null, subtotal: 1, discountTotal: 0, shippingFee: 0, recipientName: 'Synthetic', phone: '0000000000', addressLine: 'Synthetic', locality: null, province: 'HCM', countryCode: 'VN', note: null, createdAt: '2026-10-10T00:00:00Z', createAccountConsent: false, items: [{ id: '1', variantId: '1', productName: 'Synthetic', sku: 'SYN', variant: 'Black', quantity: 1, unitPrice: 1, unitDiscount: 0, lineTotal: 1 }], timeline: [] }
  const response = { snapshot, version: detail.version, paymentSummary: { confirmed: 0, refunded: 0, refundPending: 0, remaining: 1 }, allowedActions: ['Reviewing'], customerId: null, customerEmail: 'synthetic@example.invalid', notes: [], history: [], payments: [], refunds: [], reservations: [] }
  expect(decodeOrderDetail(response, true).allowedActions).toEqual(['Reviewing']); expect(() => decodeOrderDetail(response, false)).toThrow(); expect(() => decodeOrderDetail({ ...response, allowedActions: ['Cancelled'] }, true)).toThrow()
})
