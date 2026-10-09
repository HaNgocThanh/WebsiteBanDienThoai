import { expect, test } from 'vitest'
import { decodeCartVariants } from './cartDisplay'
const row = { variantId: '2', productName: 'Synthetic', productSlug: 'synthetic', sku: 'SYNTHETIC', color: 'Black', storageGb: 128, ramGb: 8, imageUrl: null, imageAltText: null }
test('cart display accepts absent image/missing variant but rejects unrelated IDs and unsafe images', () => {
  expect(decodeCartVariants([row], ['2'])).toEqual([row]); expect(decodeCartVariants([], ['2'])).toEqual([])
  for (const imageUrl of ['https://external.invalid/x.png', '//external.invalid/x.png', '/api/v1/catalog-images/../../x.png']) expect(() => decodeCartVariants([{ ...row, imageUrl }], ['2'])).toThrow()
  expect(() => decodeCartVariants([row], ['3'])).toThrow(); expect(() => decodeCartVariants([row, row], ['2', '3'])).toThrow()
})
