import { createContext, useContext } from 'react'
import { parseEntityId } from '../lib/contracts'
import { ApiError, isRecord } from '../services/errors'

export interface CartItem { variantId: string; quantity: number }
export const cartStorageKey = 'phonestore.cart.v1'
export function decodeCart(value: unknown): CartItem[] {
  if (!Array.isArray(value) || value.length > 100) throw new TypeError('Invalid cart')
  const merged = new Map<string, number>()
  for (const item of value) {
    if (!isRecord(item) || typeof item.quantity !== 'number' || !Number.isInteger(item.quantity) || item.quantity < 1 || item.quantity > 2147483647) throw new TypeError('Invalid quantity')
    const id = parseEntityId(item.variantId), quantity = (merged.get(id) ?? 0) + item.quantity
    if (quantity > 2147483647) throw new TypeError('Invalid merged quantity')
    merged.set(id, quantity)
  }
  return [...merged].map(([variantId, quantity]) => ({ variantId, quantity }))
}
export function readCart() { const raw = window.localStorage.getItem(cartStorageKey); return raw === null ? [] : decodeCart(JSON.parse(raw) as unknown) }
export interface CartState { items: CartItem[]; error?: ApiError }
interface CartActions extends CartState { add: (id: string, quantity: number) => boolean; update: (id: string, quantity: number) => boolean; remove: (id: string) => void; clear: () => void }
export const CartContext = createContext<CartActions | null>(null)
export function useCart() { const cart = useContext(CartContext); if (!cart) throw new Error('Cart provider missing'); return cart }
