import { parseEntityId, parseMoney } from '../lib/contracts'
import { request } from './client'
import { isRecord } from './errors'
import type { CartItem } from '../cart/CartContext'
import type { Destination } from './locations'

export interface QuoteLine extends CartItem { productName: string; productSlug: string; sku: string; color: string; storageGb: number; ramGb: number; available: number; unitPrice: number; unitDiscount: number; lineTotal: number; promotionName: string | null }
export interface Quote { items: QuoteLine[]; subtotal: number; discountTotal: number; shippingFee: number; grandTotal: number; currency: 'VND'; quoteHash: string }
function str(v: unknown) { if (typeof v !== 'string') throw new TypeError('Invalid text'); return v }
function int(v: unknown, min: number, max = 2147483647) { if (typeof v !== 'number' || !Number.isInteger(v) || v < min || v > max) throw new TypeError('Invalid quantity'); return v }
export function decodeQuote(v: unknown): Quote {
  if (!isRecord(v) || !Array.isArray(v.items) || v.items.length < 1 || v.items.length > 100 || v.currency !== 'VND' || typeof v.quoteHash !== 'string' || !/^[a-f0-9]{64}$/.test(v.quoteHash)) throw new TypeError('Invalid quote')
  const items = v.items.map(item => {
    if (!isRecord(item)) throw new TypeError('Invalid line')
    const line = { variantId: parseEntityId(item.variantId), quantity: int(item.quantity, 1), productName: str(item.productName), productSlug: str(item.productSlug), sku: str(item.sku), color: str(item.color), storageGb: int(item.storageGb, 1, 65536), ramGb: int(item.ramGb, 1, 1024), available: int(item.available, 0), unitPrice: parseMoney(item.unitPrice), unitDiscount: parseMoney(item.unitDiscount), lineTotal: parseMoney(item.lineTotal), promotionName: item.promotionName === null ? null : str(item.promotionName) }
    if (!/^[a-z0-9]+(-[a-z0-9]+)*$/.test(line.productSlug) || line.unitDiscount > line.unitPrice || line.quantity > line.available || BigInt(line.lineTotal) !== (BigInt(line.unitPrice) - BigInt(line.unitDiscount)) * BigInt(line.quantity)) throw new TypeError('Invalid line arithmetic')
    return line
  })
  const result: Quote = { items, subtotal: parseMoney(v.subtotal), discountTotal: parseMoney(v.discountTotal), shippingFee: parseMoney(v.shippingFee), grandTotal: parseMoney(v.grandTotal), currency: 'VND', quoteHash: v.quoteHash }
  if (new Set(items.map(x => x.variantId)).size !== items.length || BigInt(result.subtotal) !== items.reduce((n, x) => n + BigInt(x.unitPrice) * BigInt(x.quantity), 0n) || BigInt(result.discountTotal) !== items.reduce((n, x) => n + BigInt(x.unitDiscount) * BigInt(x.quantity), 0n) || BigInt(result.grandTotal) !== BigInt(result.subtotal) - BigInt(result.discountTotal) + BigInt(result.shippingFee)) throw new TypeError('Invalid quote arithmetic')
  return result
}
export const quotes = {
  get: (items: CartItem[], destination: Destination, quoteHash?: string, signal?: AbortSignal) => request('/api/v1/checkout/quote', value => {
    const quote = decodeQuote(value)
    if (quote.items.length !== items.length || quote.items.some(x => !items.some(y => x.variantId === y.variantId && x.quantity === y.quantity))) throw new TypeError('Wrong cart response')
    return quote
  }, { method: 'POST', body: { items: items.map(x => ({ variantId: x.variantId, quantity: x.quantity })), shippingAddress: { ...destination, addressLine: destination.addressLine.trim(), countryCode: 'VN' }, ...(quoteHash ? { quoteHash } : {}) }, signal }),
}
