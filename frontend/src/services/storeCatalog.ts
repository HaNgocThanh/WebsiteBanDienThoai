import { parseEntityId, parseMoney, pagedDecoder } from '../lib/contracts'
import { decodeImage, decodeLookup } from './catalog'
import type { CatalogImage, Lookup } from './catalog'
import { request } from './client'
import { isRecord } from './errors'

export interface StoreSummary { id: string; name: string; slug: string; brandId: string; categoryId: string; minPrice: number | null; imageUrl: string | null }
export interface StoreVariant { id: string; sku: string; color: string; storageGb: number; ramGb: number; price: number; available: number }
export interface StoreProduct { id: string; name: string; slug: string; description: string; specificationsJson: string | null; brand: Lookup; category: Lookup; variants: StoreVariant[]; images: CatalogImage[] }
function text(v: unknown): string { if (typeof v !== 'string') throw new TypeError('Invalid text'); return v }
function slug(v: unknown): string { const value = text(v); if (!/^[a-z0-9]+(-[a-z0-9]+)*$/.test(value) || value.length > 220) throw new TypeError('Invalid slug'); return value }
function integer(v: unknown, min: number, max: number): number { if (typeof v !== 'number' || !Number.isSafeInteger(v) || v < min || v > max) throw new TypeError('Invalid quantity'); return v }
export function managedImageUrl(v: unknown): string { const s = text(v); if (!/^\/api\/v1\/catalog-images\/[0-9a-f]{32}\.png$/.test(s)) throw new TypeError('Invalid managed URL'); return s }
function publicLookup(v: unknown): Lookup { const value = decodeLookup(v); if (!value.isActive) throw new TypeError('Inactive lookup in public catalog'); return value }
export function decodeStoreSummary(v: unknown): StoreSummary {
  if (!isRecord(v)) throw new TypeError('Invalid product')
  return { id: parseEntityId(v.id), name: text(v.name), slug: slug(v.slug), brandId: parseEntityId(v.brandId), categoryId: parseEntityId(v.categoryId), minPrice: v.minPrice === null ? null : parseMoney(v.minPrice), imageUrl: v.imageUrl === null ? null : managedImageUrl(v.imageUrl) }
}
export function decodeStoreProduct(v: unknown): StoreProduct {
  if (!isRecord(v) || !Array.isArray(v.variants) || v.variants.length === 0 || !Array.isArray(v.images)) throw new TypeError('Invalid product detail')
  const variants = v.variants.map(x => {
    if (!isRecord(x)) throw new TypeError('Invalid variant')
    return { id: parseEntityId(x.id), sku: text(x.sku), color: text(x.color), storageGb: integer(x.storageGb, 1, 65536), ramGb: integer(x.ramGb, 1, 1024), price: parseMoney(x.price), available: integer(x.available, 0, 2147483647) }
  })
  const images = v.images.map(decodeImage)
  if (new Set(variants.map(x => x.id)).size !== variants.length || images.some(x => x.variantId !== null && !variants.some(y => y.id === x.variantId))) throw new TypeError('Invalid public relationship')
  return { id: parseEntityId(v.id), name: text(v.name), slug: slug(v.slug), description: text(v.description), specificationsJson: v.specificationsJson === null ? null : text(v.specificationsJson), brand: publicLookup(v.brand), category: publicLookup(v.category), variants, images }
}
function lookups(v: unknown): Lookup[] { if (!Array.isArray(v)) throw new TypeError('Invalid lookup list'); return v.map(publicLookup) }
export const storeCatalog = {
  brands: (signal?: AbortSignal) => request('/api/v1/brands', lookups, { signal }),
  categories: (signal?: AbortSignal) => request('/api/v1/categories', lookups, { signal }),
  products: (query: URLSearchParams, signal?: AbortSignal) => request('/api/v1/products?' + query, pagedDecoder(decodeStoreSummary), { signal }),
  product: (value: string, signal?: AbortSignal) => request('/api/v1/products/' + slug(value), decodeStoreProduct, { signal }),
}
