export interface PendingImageUpload { key: string; variantId: string; altText: string; sortOrder: string }
const storageKey = (productId: string) => `phonestore.catalog-upload.${productId}`
export function readImageUpload(productId: string): PendingImageUpload | undefined {
  const raw = sessionStorage.getItem(storageKey(productId))
  if (!raw) return undefined
  const value: unknown = JSON.parse(raw)
  if (!value || typeof value !== 'object') throw new Error('Invalid upload session')
  const v = value as Record<string, unknown>
  if (typeof v.key !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(v.key) || typeof v.variantId !== 'string' || !/^(?:[1-9]\d*|)$/.test(v.variantId) || typeof v.altText !== 'string' || v.altText.length > 200 || typeof v.sortOrder !== 'string' || !/^\d+$/.test(v.sortOrder) || Number(v.sortOrder) > 2147483647) throw new Error('Invalid upload session')
  return { key: v.key, variantId: v.variantId, altText: v.altText, sortOrder: v.sortOrder }
}
export function writeImageUpload(productId: string, value?: PendingImageUpload) {
  if (value) sessionStorage.setItem(storageKey(productId), JSON.stringify(value))
  else sessionStorage.removeItem(storageKey(productId))
}
