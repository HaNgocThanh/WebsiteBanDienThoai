import { parseEntityId, parseMoney } from '../lib/contracts'
import { parseVersion } from './catalog'
import { request } from './client'
import { isRecord } from './errors'
export type PaymentArea = 'me' | 'admin' | 'guest' | 'checkout'
export interface Payment { id: string; method: 'COD' | 'BankTransfer'; status: 'Pending' | 'Confirmed' | 'Rejected'; amount: number; version: string; createdAt: string; confirmedAt: string | null; reference: string | null; note: string | null }
export const paymentStates = { Unpaid: 'Chưa thanh toán', PendingVerification: 'Đang chờ thanh toán', PartiallyPaid: 'Đã nhận một phần', Paid: 'Đã nhận đủ', RefundPending: 'Chờ hoàn tiền', Refunded: 'Đã hoàn tiền', PartiallyRefunded: 'Đã hoàn một phần' }
export interface PaymentPage { items: Payment[]; page: number; pageSize: number; totalCount: number; summary: { confirmed: number; refundPending: number; refunded: number; remaining: number; status: keyof typeof paymentStates }; canPay: boolean; canRecordCod: boolean; method: Payment['method']; sandboxConfigured: boolean }
export interface SePayCheckout { action: string; fields: { name: string; value: string }[]; invoice: string; paymentId: string; amount: number }
export interface CodDraft { amount: number; note: string; operationKey: string }
function record(v: unknown) { if (!isRecord(v)) throw new TypeError(); return v }
function text(v: unknown) { if (typeof v !== 'string') throw new TypeError(); return v }
function nullable(v: unknown) { return v === null ? null : text(v) }
function bool(v: unknown) { if (typeof v !== 'boolean') throw new TypeError(); return v }
function utc(v: unknown) { const s = text(v); if (!s.endsWith('Z') || Number.isNaN(Date.parse(s))) throw new TypeError(); return s }
function method(v: unknown): Payment['method'] { if (v !== 'COD' && v !== 'BankTransfer') throw new TypeError(); return v }
function integer(v: unknown, min: number, max = Number.MAX_SAFE_INTEGER) { if (!Number.isSafeInteger(v) || Number(v) < min || Number(v) > max) throw new TypeError(); return Number(v) }
export function decodePayment(v: unknown): Payment { const p = record(v); if (!['Pending', 'Confirmed', 'Rejected'].includes(text(p.status))) throw new TypeError(); return { id: parseEntityId(p.id), method: method(p.method), status: p.status as Payment['status'], amount: parseMoney(p.amount), version: parseVersion(p.version), createdAt: utc(p.createdAt), confirmedAt: p.confirmedAt === null ? null : utc(p.confirmedAt), reference: nullable(p.reference), note: nullable(p.note) } }
export function decodePaymentPage(v: unknown): PaymentPage {
  const p = record(v), summary = record(p.summary); if (!Array.isArray(p.items) || !Object.hasOwn(paymentStates, text(summary.status))) throw new TypeError()
  return { items: p.items.map(decodePayment), page: integer(p.page, 1, 2147483647), pageSize: integer(p.pageSize, 1, 100), totalCount: integer(p.totalCount, 0), summary: { confirmed: parseMoney(summary.confirmed), refundPending: parseMoney(summary.refundPending), refunded: parseMoney(summary.refunded), remaining: parseMoney(summary.remaining), status: summary.status as keyof typeof paymentStates }, canPay: bool(p.canPay), canRecordCod: bool(p.canRecordCod), method: method(p.method), sandboxConfigured: bool(p.sandboxConfigured) }
}
const names = ['merchant', 'operation', 'payment_method', 'order_invoice_number', 'order_amount', 'currency', 'order_description', 'success_url', 'error_url', 'cancel_url', 'signature']
export function decodeSePay(v: unknown): SePayCheckout {
  const p = record(v), paymentId = parseEntityId(p.paymentId), amount = parseMoney(p.amount), invoice = text(p.invoice)
  if (p.action !== 'https://pay-sandbox.sepay.vn/v1/checkout/init' || invoice !== 'PSB-' + paymentId || amount <= 0 || !Array.isArray(p.fields) || p.fields.length !== names.length) throw new TypeError()
  const fields = p.fields.map((v, i) => { const f = record(v); if (f.name !== names[i]) throw new TypeError(); return { name: text(f.name), value: text(f.value) } })
  if (fields[1].value !== 'PURCHASE' || fields[2].value !== 'BANK_TRANSFER' || fields[3].value !== invoice || fields[4].value !== String(amount) || fields[5].value !== 'VND' || !/^[A-Za-z0-9+/]{43}=$/.test(fields[10].value)) throw new TypeError()
  for (const f of fields.slice(7, 10)) { const url = new URL(f.value); if (url.protocol !== 'https:' || url.username || url.password || url.pathname !== '/payments/sepay/result') throw new TypeError() }
  return { action: p.action, fields, invoice, paymentId, amount }
}
export function paymentPath(area: PaymentArea, id: string) {
  if (area === 'guest') return '/api/v1/guest/order'
  if (area === 'checkout') { if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(id) || /^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(id)) throw new TypeError(); return '/api/v1/checkout/sessions/' + id }
  return '/api/v1/' + area + '/orders/' + parseEntityId(id)
}
export const payments = {
  list: (area: PaymentArea, id: string, page: number, signal?: AbortSignal) => request(paymentPath(area, id) + '/payments?page=' + page, decodePaymentPage, { signal }),
  checkout: (area: PaymentArea, id: string, signal?: AbortSignal) => request(paymentPath(area, id) + '/sepay-checkout', decodeSePay, { method: 'POST', signal }),
  receipt: (id: string, draft: CodDraft, signal?: AbortSignal) => request(paymentPath('admin', id) + '/cod-receipts', decodePayment, { method: 'POST', body: draft, signal }),
}
