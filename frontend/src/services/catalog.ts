import { parseEntityId, parseMoney, pagedDecoder } from '../lib/contracts'
import { decodeEmpty, request } from './client'
import { isRecord } from './errors'

export interface Lookup { id: string; name: string; slug: string; isActive: boolean }
export interface ProductSummary extends Lookup { brandId: string; categoryId: string; version: string }
export interface Variant { id: string; productId: string; sku: string; color: string; storageGb: number; ramGb: number; price: number; isActive: boolean; version: string }
export interface CatalogImage { id: string; variantId: string | null; imageUrl: string; altText: string; sortOrder: number }
export interface Product extends ProductSummary { description: string; specificationsJson: string | null; variants: Variant[]; images: CatalogImage[] }
export type ProductInput = Pick<Product, 'name' | 'slug' | 'brandId' | 'categoryId' | 'description' | 'specificationsJson' | 'isActive'>
export type VariantInput = Omit<Variant, 'id' | 'productId' | 'version'>
export type LookupKind = 'brands' | 'categories'
function record(v: unknown): Record<string, unknown> { if (!isRecord(v)) throw new TypeError('Invalid catalog object'); return v }
function str(v: unknown): string { if (typeof v !== 'string') throw new TypeError('Invalid text'); return v }
function bool(v: unknown): boolean { if (typeof v !== 'boolean') throw new TypeError('Invalid flag'); return v }
function integer(v: unknown, min: number, max: number): number { if (typeof v !== 'number' || !Number.isSafeInteger(v) || v < min || v > max) throw new TypeError('Invalid integer'); return v }
export function parseVersion(v: unknown): string {
  const s = str(v); const bytes = atob(s)
  if (bytes.length !== 8 || btoa(bytes) !== s) throw new TypeError('Invalid version')
  return s
}
export function decodeLookup(value: unknown): Lookup {
  const v = record(value); return { id: parseEntityId(v.id), name: str(v.name), slug: str(v.slug), isActive: bool(v.isActive) }
}
export function decodeSummary(value: unknown): ProductSummary {
  const v = record(value); return { ...decodeLookup(v), brandId: parseEntityId(v.brandId), categoryId: parseEntityId(v.categoryId), version: parseVersion(v.version) }
}
export function decodeVariant(value: unknown): Variant {
  const v = record(value)
  return { id: parseEntityId(v.id), productId: parseEntityId(v.productId), sku: str(v.sku), color: str(v.color), storageGb: integer(v.storageGb, 1, 65536), ramGb: integer(v.ramGb, 1, 1024), price: parseMoney(v.price), isActive: bool(v.isActive), version: parseVersion(v.version) }
}
export function decodeImage(value: unknown): CatalogImage {
  const v = record(value); const url = str(v.imageUrl)
  if (!/^\/api\/v1\/catalog-images\/[0-9a-f]{32}\.png$/.test(url)) throw new TypeError('Invalid image URL')
  return { id: parseEntityId(v.id), variantId: v.variantId === null ? null : parseEntityId(v.variantId), imageUrl: url, altText: str(v.altText), sortOrder: integer(v.sortOrder, 0, 2147483647) }
}
export function decodeProduct(value: unknown): Product {
  const v = record(value); if (!Array.isArray(v.variants) || !Array.isArray(v.images)) throw new TypeError('Invalid children')
  const product = { ...decodeSummary(v), description: str(v.description), specificationsJson: v.specificationsJson === null ? null : str(v.specificationsJson), variants: v.variants.map(decodeVariant), images: v.images.map(decodeImage) }
  if (product.variants.some(x => x.productId !== product.id) || product.images.some(x => x.variantId !== null && !product.variants.some(y => y.id === x.variantId))) throw new TypeError('Invalid catalog relationship')
  return product
}
const root = '/api/v1/admin'
export const catalog = {
  lookups: (kind: LookupKind, signal?: AbortSignal) => request(`${root}/${kind}`, v => { if (!Array.isArray(v)) throw new TypeError('Invalid list'); return v.map(decodeLookup) }, { signal }),
  saveLookup: (kind: LookupKind, body: Omit<Lookup, 'id'>, id?: string, signal?: AbortSignal) => request(`${root}/${kind}${id ? '/' + parseEntityId(id) : ''}`, decodeLookup, { method: id ? 'PATCH' : 'POST', body, signal }),
  products: (query: URLSearchParams, signal?: AbortSignal) => request(`${root}/products?${query}`, pagedDecoder(decodeSummary), { signal }),
  product: (id: string, signal?: AbortSignal) => request(`${root}/products/${parseEntityId(id)}`, decodeProduct, { signal }),
  saveProduct: (body: ProductInput, current?: Product, signal?: AbortSignal) => request(`${root}/products${current ? '/' + current.id : ''}`, decodeProduct, { method: current ? 'PATCH' : 'POST', body, version: current?.version, signal }),
  saveVariant: (productId: string, body: VariantInput, current?: Variant, signal?: AbortSignal) => request(current ? `${root}/variants/${current.id}` : `${root}/products/${parseEntityId(productId)}/variants`, decodeVariant, { method: current ? 'PATCH' : 'POST', body, version: current?.version, signal }),
  upload: (productId: string, form: FormData, signal?: AbortSignal) => request(`${root}/products/${parseEntityId(productId)}/images`, decodeImage, { method: 'POST', form, signal }),
  deleteImage: (id: string, signal?: AbortSignal) => request(`${root}/product-images/${parseEntityId(id)}`, decodeEmpty, { method: 'DELETE', signal }),
}
