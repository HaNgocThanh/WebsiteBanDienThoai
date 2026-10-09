import { checkoutKey } from '../services/orders'
import { ApiError, isRecord } from '../services/errors'
import { decodeCart } from '../cart/CartContext'
import type { CartItem } from '../cart/CartContext'
// Only server key and public cart IDs/quantities; never store order body, contact, address or email token.
export const pendingCheckoutStorageKey = 'phonestore.checkout.v1'
export interface PendingCheckout { key: string; items: CartItem[] }
export function cartMatches(a: CartItem[], b: CartItem[]) { return a.length === b.length && a.every(x => b.some(y => x.variantId === y.variantId && x.quantity === y.quantity)) }
export function readPendingCheckout(): PendingCheckout | null {
  try { const raw = sessionStorage.getItem(pendingCheckoutStorageKey); if (raw === null) return null; const v: unknown = JSON.parse(raw); if (!isRecord(v)) throw new TypeError(); return { key: checkoutKey(v.key), items: decodeCart(v.items) } }
  catch { throw new ApiError(0, 'CHECKOUT_STORAGE_UNAVAILABLE') }
}
export function savePendingCheckout(v: PendingCheckout | null) {
  try { if (v) sessionStorage.setItem(pendingCheckoutStorageKey, JSON.stringify({ key: checkoutKey(v.key), items: decodeCart(v.items) })); else sessionStorage.removeItem(pendingCheckoutStorageKey) }
  catch { throw new ApiError(0, 'CHECKOUT_STORAGE_UNAVAILABLE') }
}
