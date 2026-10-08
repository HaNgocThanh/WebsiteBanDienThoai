import { expect, test } from 'vitest'
import { decodeStoreProduct, decodeStoreSummary } from './storeCatalog'

const lookup = { id: '1', name: 'Synthetic', slug: 'synthetic', isActive: true }
const variant = { id: '9007199254740993', sku: 'SYNTHETIC', color: 'Black', storageGb: 128, ramGb: 8, price: 25000000, available: 3 }
const product = { id: '1', name: 'Synthetic phone', slug: 'synthetic-phone', description: '<script>synthetic</script>', specificationsJson: null, brand: lookup, category: lookup, variants: [variant], images: [] }
test('public decoder preserves bigint and rejects unsafe prices and invalid availability', () => {
  expect(decodeStoreProduct(product).variants[0].id).toBe(variant.id)
  for (const change of [{ price: -1 }, { price: 1.5 }, { price: 9007199254740992 }, { available: -1 }, { available: 2147483648 }, { available: '3' }]) expect(() => decodeStoreProduct({ ...product, variants: [{ ...variant, ...change }] })).toThrow()
})
test('public detail rejects inactive parents, empty or duplicate variants and foreign images', () => {
  for (const change of [{ brand: { ...lookup, isActive: false } }, { variants: [] }, { variants: [variant, variant] }, { images: [{ id: '1', variantId: '2', sortOrder: 0, altText: 'Test', imageUrl: '/api/v1/catalog-images/' + 'a'.repeat(32) + '.png' }] }]) expect(() => decodeStoreProduct({ ...product, ...change })).toThrow()
})
test('public summary rejects external images, unsafe money and invalid slug', () => {
  const summary = { id: '1', name: 'Test', slug: 'test', brandId: '1', categoryId: '1', minPrice: 0, imageUrl: null }
  expect(decodeStoreSummary(summary).minPrice).toBe(0)
  for (const change of [{ minPrice: 0.5 }, { imageUrl: 'https://external.invalid/image.png' }, { slug: '../secret' }]) expect(() => decodeStoreSummary({ ...summary, ...change })).toThrow()
})
