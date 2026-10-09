import { parseEntityId } from '../lib/contracts'
import { request } from './client'
import { isRecord } from './errors'

export interface CartVariant { variantId: string; productName: string; productSlug: string; sku: string; color: string; storageGb: number; ramGb: number; imageUrl: string | null; imageAltText: string | null }
export function decodeCartVariants(value: unknown, ids: string[]): CartVariant[] {
  if (!Array.isArray(value) || value.length > ids.length) throw new TypeError('Invalid cart display')
  const result = value.map(v => {
    if (!isRecord(v)) throw new TypeError('Invalid variant')
    const variantId = parseEntityId(v.variantId)
    if (!ids.includes(variantId) || typeof v.productName !== 'string' || !v.productName.trim() || typeof v.productSlug !== 'string' || !/^[a-z0-9]+(-[a-z0-9]+)*$/.test(v.productSlug)
      || typeof v.sku !== 'string' || typeof v.color !== 'string' || typeof v.storageGb !== 'number' || !Number.isInteger(v.storageGb) || v.storageGb < 1 || v.storageGb > 65536
      || typeof v.ramGb !== 'number' || !Number.isInteger(v.ramGb) || v.ramGb < 1 || v.ramGb > 1024
      || (v.imageUrl !== null && (typeof v.imageUrl !== 'string' || !/^\/api\/v1\/catalog-images\/[0-9a-f]{32}\.png$/.test(v.imageUrl)))
      || (v.imageAltText !== null && typeof v.imageAltText !== 'string')) throw new TypeError('Invalid variant display')
    return { variantId, productName: v.productName, productSlug: v.productSlug, sku: v.sku, color: v.color, storageGb: v.storageGb, ramGb: v.ramGb, imageUrl: v.imageUrl, imageAltText: v.imageAltText }
  })
  if (new Set(result.map(v => v.variantId)).size !== result.length) throw new TypeError('Duplicate variant display')
  return result
}
export const cartDisplay = {
  get: (ids: string[], signal?: AbortSignal) => request('/api/v1/catalog/variants?' + new URLSearchParams(ids.map(id => ['variantIds', parseEntityId(id)])), value => decodeCartVariants(value, ids), { signal }),
}
