import { formatVietnamTime, pagedDecoder, parseEntityId, parseMoney } from '../lib/contracts'
import { decodeEmpty, request } from './client'
import { ApiError, isRecord } from './errors'
import { decodeGuestOrder, orderStatuses } from './orders'
import type { GuestOrder, PaymentMethod, PlacedOrder } from './orders'
function text(v: unknown) { if (typeof v !== 'string') throw new TypeError('Invalid text'); return v }
function utc(v: unknown) { const s = text(v); formatVietnamTime(s); return s }
function array(v: unknown) { if (!Array.isArray(v)) throw new TypeError('Invalid array'); return v }
function record(v: unknown) { if (!isRecord(v)) throw new TypeError('Invalid object'); return v }
function uuid(v: unknown) { const s = text(v); if (!/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(s)) throw new TypeError('Invalid UUID'); return s }
function status(v: unknown): PlacedOrder['status'] { if (!Object.hasOwn(orderStatuses, text(v))) throw new TypeError('Invalid status'); return v as PlacedOrder['status'] }
function method(v: unknown): PaymentMethod { if (v !== 'COD' && v !== 'BankTransfer') throw new TypeError('Invalid method'); return v }
export interface OrderSummary { id: string; orderNumber: string; status: PlacedOrder['status']; paymentMethod: PaymentMethod; grandTotal: number; currency: 'VND'; createdAt: string; recipientName: string; customerId?: string | null; customerEmail?: string }
export interface Note { id: string; actorUserId: string; text: string; createdAt: string }
export interface OrderDetail { snapshot: GuestOrder; version: string; paymentSummary: { confirmed: number; refundPending: number; refunded: number; remaining: number }; allowedActions: string[]; customerId?: string | null; customerEmail?: string; notes?: Note[]; history?: { id: string; fromStatus: PlacedOrder['status'] | null; toStatus: PlacedOrder['status']; actorUserId: string | null; actorType: string; reason: string | null; createdAt: string }[]; payments?: { id: string; method: PaymentMethod; status: string; amount: number; createdAt: string }[]; refunds?: { id: string; paymentId: string; status: string; merchandiseAmount: number; shippingAmount: number; createdAt: string }[]; reservations?: { orderItemId: string; variantId: string; quantity: number; status: string; expiresAt: string | null }[] }
export function decodeOrderSummary(v: unknown, admin = false): OrderSummary {
  const r = record(v); if (r.currency !== 'VND' || !/^PS[0-9a-f]{28}$/.test(text(r.orderNumber))) throw new TypeError('Invalid order')
  return { id: parseEntityId(r.id), orderNumber: text(r.orderNumber), status: status(r.status), paymentMethod: method(r.paymentMethod), grandTotal: parseMoney(r.grandTotal), currency: 'VND', createdAt: utc(r.createdAt), recipientName: text(r.recipientName), ...(admin ? { customerId: r.customerId === null ? null : uuid(r.customerId), customerEmail: text(r.customerEmail) } : {}) }
}
export function decodeNote(v: unknown): Note { const n = record(v); return { id: parseEntityId(n.id), actorUserId: uuid(n.actorUserId), text: text(n.text), createdAt: utc(n.createdAt) } }
export function decodeOrderDetail(v: unknown, admin = false): OrderDetail {
  const r = record(v), m = record(r.paymentSummary); if (!/^[A-Za-z0-9+/]{11}=$/.test(text(r.version)) || array(r.allowedActions).length !== 0) throw new TypeError('Invalid detail')
  const result: OrderDetail = { snapshot: decodeGuestOrder(r.snapshot), version: text(r.version), paymentSummary: { confirmed: parseMoney(m.confirmed), refundPending: parseMoney(m.refundPending), refunded: parseMoney(m.refunded), remaining: parseMoney(m.remaining) }, allowedActions: [] }
  if (admin) { result.customerId = r.customerId === null ? null : uuid(r.customerId); result.customerEmail = text(r.customerEmail); result.notes = array(r.notes).map(decodeNote)
    result.history = array(r.history).map(v => { const h = record(v); if (!['Admin', 'Customer', 'System'].includes(text(h.actorType))) throw new TypeError(); return { id: parseEntityId(h.id), fromStatus: h.fromStatus === null ? null : status(h.fromStatus), toStatus: status(h.toStatus), actorUserId: h.actorUserId === null ? null : uuid(h.actorUserId), actorType: text(h.actorType), reason: h.reason === null ? null : text(h.reason), createdAt: utc(h.createdAt) } })
    result.payments = array(r.payments).map(v => { const p = record(v); if (!['Pending', 'Confirmed', 'Rejected'].includes(text(p.status))) throw new TypeError(); return { id: parseEntityId(p.id), method: method(p.method), status: text(p.status), amount: parseMoney(p.amount), createdAt: utc(p.createdAt) } })
    result.refunds = array(r.refunds).map(v => { const p = record(v); if (!['Pending', 'Completed', 'Failed'].includes(text(p.status))) throw new TypeError(); return { id: parseEntityId(p.id), paymentId: parseEntityId(p.paymentId), status: text(p.status), merchandiseAmount: parseMoney(p.merchandiseAmount), shippingAmount: parseMoney(p.shippingAmount), createdAt: utc(p.createdAt) } })
    result.reservations = array(r.reservations).map(v => { const p = record(v); if (!['Active', 'Released', 'Consumed'].includes(text(p.status)) || !Number.isSafeInteger(p.quantity) || Number(p.quantity) < 1) throw new TypeError(); return { orderItemId: parseEntityId(p.orderItemId), variantId: parseEntityId(p.variantId), quantity: Number(p.quantity), status: text(p.status), expiresAt: p.expiresAt === null ? null : utc(p.expiresAt) } })
  }
  return result
}
// Date input denotes a Vietnam calendar day; the UI end day is inclusive.
export function vietnamBoundary(day: string, nextDay = false) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(day) || Number(day.slice(0, 4)) < 1 || Number(day.slice(0, 4)) > 9998) throw new ApiError(400, 'INVALID_FILTER')
  const date = new Date(day + 'T00:00:00+07:00'); if (Number.isNaN(date.valueOf()) || new Date(date.valueOf() + 7 * 3600000).toISOString().slice(0, 10) !== day) throw new ApiError(400, 'INVALID_FILTER')
  return new Date(date.valueOf() + (nextDay ? 86400000 : 0)).toISOString()
}
export function orderQuery(params: URLSearchParams, admin: boolean) {
  const q = new URLSearchParams(); for (const key of ['status', 'page', 'pageSize', ...(admin ? ['email', 'customerId'] : [])]) if (params.get(key)) q.set(key, params.get(key)!)
  const from = params.get('fromDate'), to = params.get('toDate'); if (from) q.set('from', vietnamBoundary(from)); if (to) q.set('to', vietnamBoundary(to, true)); if (from && to && from > to) throw new ApiError(400, 'INVALID_FILTER'); return q
}
export interface Notice { id: string; orderId: string | null; type: string; title: string; body: string; createdAt: string; readAt: string | null }
export function decodeNotice(v: unknown): Notice { const n = record(v); return { id: parseEntityId(n.id), orderId: n.orderId === null ? null : parseEntityId(n.orderId), type: text(n.type), title: text(n.title), body: text(n.body), createdAt: utc(n.createdAt), readAt: n.readAt === null ? null : utc(n.readAt) } }
export const orderManagement = {
  list: (params: URLSearchParams, admin: boolean, signal?: AbortSignal) => request('/api/v1/' + (admin ? 'admin' : 'me') + '/orders?' + orderQuery(params, admin), pagedDecoder(v => decodeOrderSummary(v, admin)), { signal }),
  detail: (id: string, admin: boolean, signal?: AbortSignal) => request('/api/v1/' + (admin ? 'admin' : 'me') + '/orders/' + parseEntityId(id), v => decodeOrderDetail(v, admin), { signal }),
  note: (id: string, text: string, operationKey: string, signal?: AbortSignal) => request('/api/v1/admin/orders/' + parseEntityId(id) + '/notes', decodeNote, { method: 'POST', body: { text, operationKey }, signal }),
  notices: (params: URLSearchParams, signal?: AbortSignal, admin = false) => request('/api/v1/' + (admin ? 'admin' : 'me') + '/notifications?' + params, v => { const page = pagedDecoder(decodeNotice)(v), r = record(v); if (!Number.isSafeInteger(r.unreadCount) || Number(r.unreadCount) < 0) throw new TypeError(); return { ...page, unreadCount: Number(r.unreadCount) } }, { signal }),
  read: (id: string, signal?: AbortSignal, admin = false) => request('/api/v1/' + (admin ? 'admin' : 'me') + '/notifications/' + parseEntityId(id) + '/read', decodeEmpty, { method: 'POST', signal }),
}
