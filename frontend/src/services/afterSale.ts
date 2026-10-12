import { parseEntityId, parseMoney, formatVietnamTime } from '../lib/contracts'
import { parseVersion } from './catalog'
import { request } from './client'
import { isRecord } from './errors'
import { orderStatuses } from './orders'
export type AfterSaleArea = 'admin' | 'me' | 'guest'
export interface AfterSaleData { orderId: string; status: keyof typeof orderStatuses; version: string; canCancel: boolean; canRequest: boolean; requests: { id: string; status: 'Pending' | 'Approved' | 'Rejected'; reason: string; version: string; reviewNote: string | null; createdAt: string }[]; refunds: { id: string; paymentId: string; status: 'Pending' | 'Completed' | 'Failed'; merchandiseAmount: number; shippingAmount: number; version: string; reason: string | null; reference: string | null }[] }
export interface AfterSaleDraft { reason: string; operationKey: string; decision?: 'Approve' | 'Reject'; goodsRecovered?: boolean; reference?: string; merchandiseAmount?: number; shippingAmount?: number }
function record(v: unknown) { if (!isRecord(v)) throw new TypeError(); return v }
function text(v: unknown): string { if (typeof v !== 'string') throw new TypeError(); return v }
function nullable(v: unknown) { return v === null ? null : text(v) }
function oneOf<T extends string>(v: unknown, values: readonly T[]): T { if (!values.includes(v as T)) throw new TypeError(); return v as T }
export function decodeAfterSale(v: unknown): AfterSaleData {
  const r = record(v); if (typeof r.canCancel !== 'boolean' || typeof r.canRequest !== 'boolean' || !Array.isArray(r.requests) || !Array.isArray(r.refunds)) throw new TypeError()
  return { orderId: parseEntityId(r.orderId), status: oneOf(r.status, Object.keys(orderStatuses) as (keyof typeof orderStatuses)[]), version: parseVersion(r.version), canCancel: r.canCancel, canRequest: r.canRequest,
    requests: r.requests.map(v => { const x = record(v); const createdAt = text(x.createdAt); formatVietnamTime(createdAt); return { id: parseEntityId(x.id), status: oneOf(x.status, ['Pending', 'Approved', 'Rejected'] as const), reason: text(x.reason), version: parseVersion(x.version), reviewNote: nullable(x.reviewNote), createdAt } }),
    refunds: r.refunds.map(v => { const x = record(v); return { id: parseEntityId(x.id), paymentId: parseEntityId(x.paymentId), status: oneOf(x.status, ['Pending', 'Completed', 'Failed'] as const), merchandiseAmount: parseMoney(x.merchandiseAmount), shippingAmount: parseMoney(x.shippingAmount), version: parseVersion(x.version), reason: nullable(x.reason), reference: nullable(x.reference) } }) }
}
export function afterSalePath(area: AfterSaleArea, id: string) { return area === 'guest' ? '/api/v1/guest/order' : '/api/v1/' + area + '/orders/' + parseEntityId(id) }
export const afterSale = {
  read: (area: AfterSaleArea, id: string, signal?: AbortSignal) => request(afterSalePath(area, id) + '/after-sale', decodeAfterSale, { signal }),
  mutate: (path: string, version: string, body: AfterSaleDraft, signal?: AbortSignal) => request(path, v => { const r = record(v); return { orderId: parseEntityId(r.orderId), version: parseVersion(r.version) } }, { method: 'POST', body, headers: { 'If-Match': '"' + parseVersion(version) + '"' }, signal }),
}
