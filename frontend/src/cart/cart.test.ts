import { expect, test } from 'vitest'
import { decodeCart } from './CartContext'
import { decodeQuote } from '../services/quotes'

export const quoteFixture = { items: [{ variantId: '9007199254740993', quantity: 2, productName: 'Synthetic phone', productSlug: 'synthetic-phone', sku: 'SYNTHETIC', color: 'Black', storageGb: 128, ramGb: 8, available: 5, unitPrice: 1000000, unitDiscount: 0, lineTotal: 2000000, promotionName: null }], subtotal: 2000000, discountTotal: 0, shippingFee: 30000, grandTotal: 2030000, currency: 'VND', quoteHash: 'a'.repeat(64) }
test('cart persists only bigint ID and quantity, strips attacker prices/tier and merges duplicate IDs', () => {
  expect(decodeCart([{ variantId: '9007199254740993', quantity: 1, unitPrice: 0, tier: 'Diamond' }, { variantId: '9007199254740993', quantity: 2 }])).toEqual([{ variantId: '9007199254740993', quantity: 3 }])
})
test.each([null, [{ variantId: 1, quantity: 1 }], [{ variantId: '1', quantity: 0 }], [{ variantId: '1', quantity: 1.5 }], [{ variantId: '1', quantity: 2147483648 }], [{ variantId: '1', quantity: 2147483647 }, { variantId: '1', quantity: 1 }]])('invalid stored cart %j is rejected', value => expect(() => decodeCart(value)).toThrow())
test('quote decoder verifies money arithmetic, unique IDs, currency and availability', () => {
  expect(decodeQuote(quoteFixture).grandTotal).toBe(2030000)
  for (const change of [{ grandTotal: 1 }, { currency: 'USD' }, { quoteHash: 'invalid' }, { shippingFee: 1.5 }, { items: [quoteFixture.items[0], quoteFixture.items[0]] }, { items: [{ ...quoteFixture.items[0], available: 1 }] }, { items: [{ ...quoteFixture.items[0], unitPrice: 0 }] }]) expect(() => decodeQuote({ ...quoteFixture, ...change })).toThrow()
})
