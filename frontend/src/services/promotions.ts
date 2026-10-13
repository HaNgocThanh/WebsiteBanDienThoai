import { request } from './client'
import { isRecord } from './errors'
import { parseEntityId, parseMoney, pagedDecoder } from '../lib/contracts'
import { parseVersion } from './catalog'
export interface Promotion { id: string; name: string; description: string | null; status: string; productScope: string; productIds: string[]; audienceScope: string; tierIds: string[]; discountType: string; discountValue: number; maxDiscountPerUnit: number | null; startsAt: string; endsAt: string; version: string; products: { id: string; name: string }[] }
export type PromotionInput = Omit<Promotion, 'id' | 'status' | 'version' | 'products'> & { operationKey: string }
function decode(v: unknown): Promotion {
  if (!isRecord(v) || typeof v.name !== 'string' || !(v.description === null || typeof v.description === 'string') || !['Draft','Published','Disabled'].includes(String(v.status)) || !['All','Selected'].includes(String(v.productScope)) || !['All','SelectedTiers'].includes(String(v.audienceScope)) || !['Percent','Fixed'].includes(String(v.discountType)) || !Array.isArray(v.productIds) || !Array.isArray(v.tierIds) || typeof v.discountValue !== 'number' || !Number.isFinite(v.discountValue) || v.discountValue <= 0 || typeof v.startsAt !== 'string' || typeof v.endsAt !== 'string' || !Number.isFinite(Date.parse(v.startsAt)) || !Number.isFinite(Date.parse(v.endsAt)) || Date.parse(v.startsAt) >= Date.parse(v.endsAt)) throw new TypeError('Invalid promotion')
  if (!Array.isArray(v.products)) throw new TypeError('Invalid targets')
  return { ...(v as unknown as Promotion), id: parseEntityId(v.id), version: parseVersion(v.version), productIds: v.productIds.map(parseEntityId), tierIds: v.tierIds.map(parseEntityId), maxDiscountPerUnit: v.maxDiscountPerUnit === null ? null : parseMoney(v.maxDiscountPerUnit), products: v.products.map(x => { if (!isRecord(x) || typeof x.name !== 'string') throw new TypeError('Invalid product'); return { id: parseEntityId(x.id), name: x.name } }) }
}
function receipt(v: unknown) { if (!isRecord(v) || !['Draft','Published','Disabled','Deleted'].includes(String(v.status))) throw new TypeError('Invalid receipt'); return { id: parseEntityId(v.id), status: String(v.status), version: parseVersion(v.version) } }
const root = '/api/v1/admin/promotions'
export const promotions = {
  list: (query: URLSearchParams, signal?: AbortSignal) => request(root + '?' + query, pagedDecoder(decode), { signal }),
  get: (id: string, signal?: AbortSignal) => request(root + '/' + id, decode, { signal }),
  save: (id: string | undefined, body: PromotionInput, version?: string, signal?: AbortSignal) => request(root + (id ? '/' + id : ''), receipt, { method: id ? 'PATCH' : 'POST', body, version, signal }),
  action: (id: string, action: 'publish' | 'disable' | 'delete', operationKey: string, version: string, signal?: AbortSignal) => request(root + '/' + id + (action === 'delete' ? '' : '/' + action), receipt, { method: action === 'delete' ? 'DELETE' : 'POST', body: { operationKey }, version, signal }),
}
