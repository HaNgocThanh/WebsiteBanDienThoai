import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { ApiError } from '../services/errors'
import { CartContext, cartStorageKey, decodeCart, readCart } from './CartContext'
import type { CartItem, CartState } from './CartContext'

export function CartProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<CartState>(() => { try { return { items: readCart() } } catch { return { items: [], error: new ApiError(0, 'CART_STORAGE_INVALID') } } })
  useEffect(() => {
    const sync = (event: StorageEvent) => { if (event.key === cartStorageKey || event.key === null) { try { setState({ items: readCart() }) } catch { setState({ items: [], error: new ApiError(0, 'CART_STORAGE_INVALID') }) } } }
    window.addEventListener('storage', sync); return () => window.removeEventListener('storage', sync)
  }, [])
  function save(items: CartItem[]): boolean {
    try { const valid = decodeCart(items); window.localStorage.setItem(cartStorageKey, JSON.stringify(valid)); setState({ items: valid }); return true }
    catch { setState(s => ({ ...s, error: new ApiError(0, 'CART_STORAGE_INVALID') })); return false }
  }
  return <CartContext.Provider value={{ ...state, add: (id, quantity) => !state.error && save([...state.items, { variantId: id, quantity }]), update: (id, quantity) => !state.error && save(state.items.map(x => x.variantId === id ? { ...x, quantity } : x)), remove: id => { save(state.items.filter(x => x.variantId !== id)) }, clear: () => { save([]) } }}>{children}</CartContext.Provider>
}
