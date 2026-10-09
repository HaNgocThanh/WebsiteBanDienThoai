import { expect, test } from 'vitest'
import { vietnamBoundary, orderQuery, decodeOrderSummary, decodeNotice } from './orderManagement'
test('Vietnam date range includes end day and crosses UTC year boundaries', () => {
  expect(vietnamBoundary('2026-10-09')).toBe('2026-10-08T17:00:00.000Z'); expect(vietnamBoundary('2026-12-31', true)).toBe('2026-12-31T17:00:00.000Z')
  expect(orderQuery(new URLSearchParams('fromDate=2026-10-09&toDate=2026-10-09&page=2&email=synthetic%40example.invalid'), true).get('to')).toBe('2026-10-09T17:00:00.000Z')
  for (const day of ['2026-02-30', 'bad', '0000-01-01']) expect(() => vietnamBoundary(day)).toThrow()
  expect(() => orderQuery(new URLSearchParams('fromDate=2026-10-10&toDate=2026-10-09'), false)).toThrow()
})
test('customer query never sends owner/email supplied through URL', () => { const q = orderQuery(new URLSearchParams('email=other&customerId=other&pageSize=20'), false); expect(q.get('email')).toBeNull(); expect(q.get('customerId')).toBeNull() })
test('order list and notifications preserve bigint and reject unsafe money/nonUTC times', () => {
  const order = { id: '9223372036854775807', orderNumber: 'PS' + 'a'.repeat(28), status: 'Placed', paymentMethod: 'COD', grandTotal: 1, currency: 'VND', createdAt: '2026-10-09T00:00:00Z', recipientName: 'Synthetic' }
  expect(decodeOrderSummary(order).id).toBe(order.id); expect(() => decodeOrderSummary({ ...order, id: 1 })).toThrow(); expect(() => decodeOrderSummary({ ...order, grandTotal: Number.MAX_SAFE_INTEGER + 1 })).toThrow()
  expect(() => decodeNotice({ id: '1', orderId: null, type: 'Synthetic', title: '', body: '', createdAt: '2026-10-09T07:00:00+07:00', readAt: null })).toThrow()
})
