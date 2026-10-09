import { formatVietnamTime, parseEntityId, parseMoney } from '../lib/contracts'
import { decodeEmpty, request } from './client'
import { isRecord } from './errors'
import type { CartItem } from '../cart/CartContext'
import type { Destination } from './locations'

export type PaymentMethod = 'COD' | 'BankTransfer'
export const orderStatuses = { Placed: 'Đã đặt hàng', Reviewing: 'Đang xem xét', Approved: 'Đã xác nhận', Shipping: 'Đang giao', Delivered: 'Đã giao', Completed: 'Hoàn tất', Cancelled: 'Đã hủy' }
export interface PlacedOrder { id: string; orderNumber: string; status: keyof typeof orderStatuses; grandTotal: number; currency: 'VND'; paymentMethod: PaymentMethod; paymentDueAt: string | null }
export interface OrderBody extends Destination { items: CartItem[]; recipientName: string; phone: string; email?: string; countryCode: 'VN'; paymentMethod: PaymentMethod; quoteHash: string; createAccountConsent: boolean; consentTextVersion?: string; note: string }
export interface CheckoutSession { checkoutKey: string; expiresAt: string }
export interface CheckoutResult { state: 'Issued' | 'Completed' | 'Expired'; expiresAt: string; order: PlacedOrder | null }
export interface GuestLine { id: string; variantId: string; productName: string; sku: string; variant: string; quantity: number; unitPrice: number; unitDiscount: number; lineTotal: number }
export interface GuestOrder extends PlacedOrder { subtotal: number; discountTotal: number; shippingFee: number; recipientName: string; phone: string; addressLine: string; locality: string | null; province: string; countryCode: 'VN'; note: string | null; createdAt: string; createAccountConsent: boolean; items: GuestLine[]; timeline: { fromStatus: string | null; toStatus: string; createdAt: string }[] }
function text(v: unknown): string { if (typeof v !== 'string') throw new TypeError('Invalid text'); return v }
function utc(v: unknown): string { const value = text(v); formatVietnamTime(value); return value }
function nullableText(v: unknown) { return v === null ? null : text(v) }
export function checkoutKey(v: unknown) { const key = text(v); if (!/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(key) || key === '00000000-0000-0000-0000-000000000000') throw new TypeError('Invalid key'); return key }
export function decodePlacedOrder(v: unknown): PlacedOrder {
  if (!isRecord(v) || v.currency !== 'VND' || typeof v.status !== 'string' || !Object.hasOwn(orderStatuses, v.status) || !['COD', 'BankTransfer'].includes(text(v.paymentMethod)) || !/^PS[0-9a-f]{28}$/.test(text(v.orderNumber))) throw new TypeError('Invalid order')
  const result: PlacedOrder = { id: parseEntityId(v.id), orderNumber: text(v.orderNumber), status: v.status as PlacedOrder['status'], grandTotal: parseMoney(v.grandTotal), currency: 'VND', paymentMethod: v.paymentMethod as PaymentMethod, paymentDueAt: v.paymentDueAt === null ? null : utc(v.paymentDueAt) }
  if (result.paymentMethod === 'COD' && result.paymentDueAt !== null || result.paymentMethod === 'BankTransfer' && result.paymentDueAt === null) throw new TypeError('Invalid payment deadline')
  return result
}
export function decodeSession(v: unknown): CheckoutSession { if (!isRecord(v)) throw new TypeError('Invalid session'); return { checkoutKey: checkoutKey(v.checkoutKey), expiresAt: utc(v.expiresAt) } }
export function decodeResult(v: unknown): CheckoutResult {
  if (!isRecord(v) || !['Issued', 'Completed', 'Expired'].includes(text(v.state))) throw new TypeError('Invalid result')
  const result = { state: v.state as CheckoutResult['state'], expiresAt: utc(v.expiresAt), order: v.order === null ? null : decodePlacedOrder(v.order) }
  if ((result.state === 'Completed') !== (result.order !== null)) throw new TypeError('Invalid result state'); return result
}
export function decodeGuestOrder(v: unknown): GuestOrder {
  const placed = decodePlacedOrder(v)
  if (!isRecord(v) || v.countryCode !== 'VN' || typeof v.createAccountConsent !== 'boolean' || !Array.isArray(v.items) || !v.items.length || v.items.length > 100 || !Array.isArray(v.timeline)) throw new TypeError('Invalid guest order')
  const items = v.items.map(i => {
    if (!isRecord(i) || typeof i.quantity !== 'number' || !Number.isInteger(i.quantity) || i.quantity < 1 || i.quantity > 2147483647) throw new TypeError('Invalid line')
    const line = { id: parseEntityId(i.id), variantId: parseEntityId(i.variantId), productName: text(i.productName), sku: text(i.sku), variant: text(i.variant), quantity: i.quantity, unitPrice: parseMoney(i.unitPrice), unitDiscount: parseMoney(i.unitDiscount), lineTotal: parseMoney(i.lineTotal) }
    if (line.unitDiscount > line.unitPrice || BigInt(line.lineTotal) !== (BigInt(line.unitPrice) - BigInt(line.unitDiscount)) * BigInt(line.quantity)) throw new TypeError('Invalid line total'); return line
  })
  const result: GuestOrder = { ...placed, subtotal: parseMoney(v.subtotal), discountTotal: parseMoney(v.discountTotal), shippingFee: parseMoney(v.shippingFee), recipientName: text(v.recipientName), phone: text(v.phone), addressLine: text(v.addressLine), locality: nullableText(v.locality), province: text(v.province), countryCode: 'VN', note: nullableText(v.note), createdAt: utc(v.createdAt), createAccountConsent: v.createAccountConsent, items, timeline: v.timeline.map(h => {
    if (!isRecord(h) || !Object.hasOwn(orderStatuses, text(h.toStatus)) || h.fromStatus !== null && !Object.hasOwn(orderStatuses, text(h.fromStatus))) throw new TypeError('Invalid history')
    return { fromStatus: nullableText(h.fromStatus), toStatus: text(h.toStatus), createdAt: utc(h.createdAt) }
  }) }
  if (new Set(items.map(i => i.id)).size !== items.length || BigInt(result.subtotal) !== items.reduce((sum, i) => sum + BigInt(i.unitPrice) * BigInt(i.quantity), 0n) || BigInt(result.discountTotal) !== items.reduce((sum, i) => sum + BigInt(i.unitDiscount) * BigInt(i.quantity), 0n) || BigInt(result.grandTotal) !== BigInt(result.subtotal) - BigInt(result.discountTotal) + BigInt(result.shippingFee)) throw new TypeError('Invalid totals')
  return result
}
export const orders = {
  session: () => request('/api/v1/checkout/sessions', decodeSession, { method: 'POST' }),
  result: (key: string, signal?: AbortSignal) => request(`/api/v1/checkout/sessions/${checkoutKey(key)}/result`, decodeResult, { signal }),
  place: (key: string, body: OrderBody) => request('/api/v1/orders', decodePlacedOrder, { method: 'POST', body, headers: { 'Idempotency-Key': checkoutKey(key) } }),
  requestAccess: (orderNumber: string, email: string, purpose: 'ViewOrder' | 'ClaimOrder') => request('/api/v1/guest/order-access-requests', decodeEmpty, { method: 'POST', body: { orderNumber, email, purpose } }),
  exchange: (token: string, purpose: 'ViewOrder' | 'CancelOrder') => request('/api/v1/guest/order-access/exchange', v => {
    if (!isRecord(v) || v.purpose !== purpose) throw new TypeError('Invalid grant'); return { purpose, expiresAt: utc(v.expiresAt) }
  }, { method: 'POST', body: { token, purpose } }),
  guest: (signal?: AbortSignal) => request('/api/v1/guest/order', decodeGuestOrder, { signal }),
  setup: () => request('/api/v1/guest/account-setup-requests', decodeEmpty, { method: 'POST' }),
  claim: (token: string) => request('/api/v1/me/order-claims', v => { if (!isRecord(v)) throw new TypeError('Invalid claim'); return { id: parseEntityId(v.id), orderNumber: text(v.orderNumber) } }, { method: 'POST', body: { token } }),
}
