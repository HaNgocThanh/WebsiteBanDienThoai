import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, test, vi } from 'vitest'
import { MemoryRouter } from 'react-router'
import { OrderPayments } from './OrderPayments'
import { SePayResultPage } from './SePayResultPage'
import { payments, decodeSePay } from '../services/payments'
import type { PaymentPage, SePayCheckout, Payment } from '../services/payments'
import { AuthContext } from '../auth/AuthContext'
import { ApiError } from '../services/errors'
const user = { userId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', email: 'synthetic@example.invalid', fullName: 'Synthetic', phone: null, emailConfirmed: true, roles: ['Customer', 'Admin'] }
const page: PaymentPage = { items: [], page: 1, pageSize: 20, totalCount: 0, summary: { confirmed: 0, remaining: 100, refundPending: 0, refunded: 0, status: 'Unpaid' }, method: 'BankTransfer', canPay: true, canRecordCod: false, sandboxConfigured: true }
const form: SePayCheckout = { action: 'https://pay-sandbox.sepay.vn/v1/checkout/init', fields: ['merchant', 'operation', 'payment_method', 'order_invoice_number', 'order_amount', 'currency', 'order_description', 'success_url', 'error_url', 'cancel_url', 'signature'].map((name, i) => ({ name, value: ['SYN', 'PURCHASE', 'BANK_TRANSFER', 'PSB-1', '100', 'VND', 'Synthetic', 'https://example.invalid/payments/sepay/result', 'https://example.invalid/payments/sepay/result', 'https://example.invalid/payments/sepay/result', 'a'.repeat(43) + '='][i] })), invoice: 'PSB-1', paymentId: '1', amount: 100 }
afterEach(() => { cleanup(); vi.restoreAllMocks() })
function open(element: React.ReactNode, path = '/') { render(<AuthContext.Provider value={{ status: 'authenticated', user, refresh: vi.fn(), clear: vi.fn() }}><MemoryRouter initialEntries={[path]}>{element}</MemoryRouter></AuthContext.Provider>) }
test('server signed checkout requires explicit navigation and never accepts customer amount or proof', async () => {
  vi.spyOn(payments, 'list').mockResolvedValue(page); const start = vi.spyOn(payments, 'checkout').mockResolvedValue(form)
  open(<OrderPayments id="1" area="me" />); fireEvent.click(await screen.findByRole('button', { name: 'Thanh toán qua SePay Sandbox' }))
  const button = await screen.findByRole('button', { name: 'Tiếp tục sang SePay Sandbox' }); expect(button.closest('form')?.getAttribute('action')).toBe(form.action); expect(button.closest('form')?.getAttribute('method')).toBe('post')
  expect(start).toHaveBeenCalledWith('me', '1', expect.any(AbortSignal)); expect(screen.queryByLabelText('Số tiền VND')).toBeNull(); expect(screen.queryByLabelText(/chứng từ PNG/)).toBeNull(); expect(screen.getByText('Chưa thanh toán')).toBeTruthy()
})
test('missing configuration gives a useful notice and disables initiation', async () => {
  vi.spyOn(payments, 'list').mockResolvedValue({ ...page, sandboxConfigured: false }); const start = vi.spyOn(payments, 'checkout')
  open(<OrderPayments id="1" area="me" />); const button = await screen.findByRole('button', { name: 'Thanh toán qua SePay Sandbox' }); expect(button.hasAttribute('disabled')).toBe(true); fireEvent.click(button); expect(start).not.toHaveBeenCalled()
})
test('Admin COD lost response freezes fields and retries the exact key and payload', async () => {
  vi.spyOn(payments, 'list').mockResolvedValue({ ...page, method: 'COD', canPay: false, canRecordCod: true }); const received: Payment = { id: '1', method: 'COD', status: 'Confirmed', amount: 100, createdAt: '2026-10-09T00:00:00Z', confirmedAt: '2026-10-09T00:00:00Z', version: 'AAAAAAAAAAE=', reference: null, note: 'Verified' }
  const receipt = vi.spyOn(payments, 'receipt').mockRejectedValueOnce(new ApiError(0, 'NETWORK_ERROR')).mockResolvedValue(received)
  open(<OrderPayments id="1" area="admin" />); fireEvent.change(await screen.findByLabelText('Ghi chú đối soát'), { target: { value: 'Verified' } }); fireEvent.click(screen.getByLabelText('Tôi đã kiểm tra và xác nhận số tiền thực nhận.')); fireEvent.click(screen.getByRole('button', { name: 'Ghi nhận tiền đã nhận' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Thử lại cùng yêu cầu thanh toán' })); await waitFor(() => expect(receipt).toHaveBeenCalledTimes(2)); expect(receipt.mock.calls[0][1]).toEqual(receipt.mock.calls[1][1]); expect(screen.queryByRole('button', { name: 'Thanh toán qua SePay Sandbox' })).toBeNull()
})
test('success redirect cannot claim paid or mutate payment', async () => {
  vi.spyOn(payments, 'list').mockResolvedValue(page); const start = vi.spyOn(payments, 'checkout')
  open(<SePayResultPage />, '/payments/sepay/result?orderId=1&result=success'); await screen.findByText('Chưa thanh toán'); expect(screen.queryByText('Đã nhận đủ')).toBeNull(); expect(start).not.toHaveBeenCalled()
})
test('signed form decoder rejects production destinations, reordered fields and inconsistent amounts', () => {
  expect(decodeSePay(form)).toEqual(form); expect(() => decodeSePay({ ...form, action: 'https://pay.sepay.vn/v1/checkout/init' })).toThrow(); expect(() => decodeSePay({ ...form, amount: 101 })).toThrow(); expect(() => decodeSePay({ ...form, fields: [...form.fields].reverse() })).toThrow()
})
