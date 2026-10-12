import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, test, vi } from 'vitest'
import { AfterSaleForm, OrderAfterSale } from './OrderAfterSale'
import { afterSale, decodeAfterSale } from '../services/afterSale'
import { ApiError } from '../services/errors'
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: null }) }))
afterEach(() => { cleanup(); vi.restoreAllMocks() })
test('delivered order pending recall offers rejection only and never recovery approval', async () => {
  vi.spyOn(afterSale, 'read').mockResolvedValue({ orderId: '1', status: 'Delivered', version: 'AAAAAAAAAAE=', canCancel: false, canRequest: false, refunds: [], requests: [{ id: '1', status: 'Pending', reason: 'Synthetic', reviewNote: null, createdAt: '2026-10-12T00:00:00Z', version: 'AAAAAAAAAAI=' }] })
  render(<OrderAfterSale id="1" area="admin" />); await screen.findByRole('heading', { name: 'Từ chối yêu cầu thu hồi' }); expect(screen.queryByRole('heading', { name: 'Chấp thuận thu hồi và hủy đơn' })).toBeNull()
})
test('optional note and explicit selection/confirmation produce same cancellation through lost acknowledgment retry', async () => {
  const mutate = vi.spyOn(afterSale, 'mutate').mockRejectedValueOnce(new ApiError(0, 'TIMEOUT')).mockResolvedValue({ orderId: '1', version: 'AAAAAAAAAAI=' }), reload = vi.fn()
  render(<AfterSaleForm path="/api/v1/me/orders/1/cancel" version="AAAAAAAAAAE=" title="Hủy đơn hàng" reasons={['Không còn nhu cầu']} reload={reload} />)
  expect(screen.getByRole('button', { name: 'Hủy đơn hàng' }).matches(':disabled')).toBe(true)
  fireEvent.change(screen.getByLabelText('Lý do Hủy đơn hàng'), { target: { value: 'Không còn nhu cầu' } }); fireEvent.click(screen.getByRole('checkbox')); fireEvent.click(screen.getByRole('button', { name: 'Hủy đơn hàng' }))
  const retry = await screen.findByRole('button', { name: 'Thử lại: Hủy đơn hàng' }); await waitFor(() => expect(retry.matches(':disabled')).toBe(false)); expect(screen.getByRole('combobox').matches(':disabled')).toBe(true)
  const first = mutate.mock.calls[0]; expect(first[2].reason).toBe('Không còn nhu cầu'); expect(screen.getByRole('textbox').hasAttribute('required')).toBe(false); fireEvent.click(retry); await waitFor(() => expect(reload).toHaveBeenCalledOnce()); expect(mutate.mock.calls[1].slice(0, 3)).toEqual(first.slice(0, 3))
})
test('stale refund shows error and requires reload without resubmitting', async () => {
  const mutate = vi.spyOn(afterSale, 'mutate').mockRejectedValue(new ApiError(412, 'VERSION_CONFLICT')), reload = vi.fn()
  render(<AfterSaleForm path="/api/v1/admin/refunds/1/fail" version="AAAAAAAAAAE=" title="Thất bại" reasons={['Đã kiểm tra']} reload={reload} />)
  fireEvent.change(screen.getByRole('combobox'), { target: { value: 'Đã kiểm tra' } }); fireEvent.click(screen.getByRole('checkbox')); fireEvent.submit(screen.getByRole('button', { name: 'Thất bại' }).closest('form')!)
  const refresh = await screen.findByRole('button', { name: 'Tải lại trước khi thao tác' }); expect(mutate).toHaveBeenCalledOnce(); expect(reload).not.toHaveBeenCalled(); expect(screen.getByRole('alert')).toBeTruthy(); fireEvent.click(refresh); expect(reload).toHaveBeenCalledOnce()
})
test('recovery requires evidence and an explicit unchecked recovery declaration', () => {
  render(<AfterSaleForm path="/api/v1/admin/orders/1/cancellation-requests/1/review" version="AAAAAAAAAAE=" title="Thu hồi" reasons={['Đã thu hồi']} recovery decision="Approve" reload={vi.fn()} />)
  expect(screen.getByRole('checkbox').matches(':checked')).toBe(false); expect(screen.getByLabelText('Chứng từ thu hồi hàng').hasAttribute('required')).toBe(true)
})
test('after sale decoder rejects malformed money/status/versions', () => {
  const valid = { orderId: '1', status: 'Cancelled', version: 'AAAAAAAAAAE=', canCancel: false, canRequest: false, requests: [], refunds: [{ id: '1', paymentId: '1', status: 'Pending', merchandiseAmount: 10, shippingAmount: 0, version: 'AAAAAAAAAAI=', reason: null, reference: null }] }
  expect(decodeAfterSale(valid).refunds[0].merchandiseAmount).toBe(10); expect(() => decodeAfterSale({ ...valid, status: 'Paid' })).toThrow(); expect(() => decodeAfterSale({ ...valid, version: 'bad' })).toThrow(); expect(() => decodeAfterSale({ ...valid, refunds: [{ ...valid.refunds[0], merchandiseAmount: -1 }] })).toThrow()
})
