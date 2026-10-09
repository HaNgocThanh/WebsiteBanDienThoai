import { expect, test } from 'vitest'
import { decodeGuestOrder, decodePlacedOrder, decodeResult } from './orders'
import { readPendingCheckout, savePendingCheckout, pendingCheckoutStorageKey } from '../checkout/pending'
const placed = { id: '9223372036854775807', orderNumber: 'PS' + 'a'.repeat(28), status: 'Placed', grandTotal: 1030000, currency: 'VND', paymentMethod: 'BankTransfer', paymentDueAt: '2026-10-10T10:00:00Z' }
test('order decoder preserves bigint IDs and rejects unsafe money/status/deadline', () => {
  expect(decodePlacedOrder(placed).id).toBe(placed.id)
  for (const v of [{ ...placed, id: 1 }, { ...placed, grandTotal: Number.MAX_SAFE_INTEGER + 1 }, { ...placed, status: '__proto__' }, { ...placed, paymentMethod: 'COD' }, { ...placed, paymentDueAt: '2026-10-10T10:00:00+07:00' }]) expect(() => decodePlacedOrder(v)).toThrow()
})
test('recovery state cannot fake a successful order', () => {
  expect(() => decodeResult({ state: 'Completed', expiresAt: '2026-10-10T10:00:00Z', order: null })).toThrow(); expect(() => decodeResult({ state: 'Issued', expiresAt: '2026-10-10T10:00:00Z', order: placed })).toThrow()
})
test('guest snapshot arithmetic is verified rather than accepting server-shaped garbage', () => {
  const v = { ...placed, subtotal: 1000000, discountTotal: 0, shippingFee: 30000, recipientName: 'Synthetic', phone: '0000000000', addressLine: 'Synthetic', locality: null, province: 'Đà Nẵng', countryCode: 'VN', note: null, createdAt: '2026-10-09T10:00:00Z', createAccountConsent: false, items: [{ id: '1', variantId: '2', productName: 'Synthetic', sku: 'SYN', variant: 'Black', quantity: 1, unitPrice: 1000000, unitDiscount: 0, lineTotal: 1000000 }], timeline: [{ fromStatus: null, toStatus: 'Placed', createdAt: '2026-10-09T10:00:00Z' }] }
  expect(decodeGuestOrder(v).grandTotal).toBe(1030000); expect(() => decodeGuestOrder({ ...v, shippingFee: 0 })).toThrow(); expect(() => decodeGuestOrder({ ...v, items: [{ ...v.items[0], quantity: 2 }] })).toThrow()
})
test('pending checkout only persists sanitized key/cart and rejects corrupt storage', () => {
  savePendingCheckout({ key: '11111111-1111-4111-8111-111111111111', items: [{ variantId: '2', quantity: 1 }] }); expect(Object.keys(JSON.parse(sessionStorage.getItem(pendingCheckoutStorageKey)!))).toEqual(['key', 'items']); expect(readPendingCheckout()?.items).toEqual([{ variantId: '2', quantity: 1 }]); sessionStorage.setItem(pendingCheckoutStorageKey, '{bad'); expect(() => readPendingCheckout()).toThrow()
})
