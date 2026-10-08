import { expect, test } from 'vitest'
import { decodeImage, decodeProduct, decodeVariant } from './catalog'
import { validateSpecifications } from '../lib/catalogValidation'

const variant = { id: '9007199254740993', productId: '2', sku: 'TEST', color: 'Đen', storageGb: 128, ramGb: 8, price: 25000000, isActive: true, version: 'AAAAAAAAAAE=' }
test('catalog preserves bigint strings and validates money and rowversion', () => {
  expect(decodeVariant(variant).id).toBe('9007199254740993')
  for (const change of [{ price: 1.5 }, { price: -1 }, { price: 9007199254740992 }, { id: 123 }, { version: '*' }, { storageGb: 0 }]) expect(() => decodeVariant({ ...variant, ...change })).toThrow()
})
test('managed images reject external or escaped paths', () => {
  const image = { id: '1', variantId: null, altText: 'Ảnh', sortOrder: 0, imageUrl: '/api/v1/catalog-images/' + 'a'.repeat(32) + '.png' }
  expect(decodeImage(image).imageUrl).toBe(image.imageUrl)
  for (const imageUrl of ['https://foreign.invalid/photo.png', '//foreign.invalid/a.png', 'javascript:alert(1)', '/api/v1/catalog-images/../secret', '/uploads/a.png']) expect(() => decodeImage({ ...image, imageUrl })).toThrow()
})
test('product decoder rejects a variant or image belonging to another product', () => {
  const product = { id: '2', brandId: '1', categoryId: '1', name: 'Test', slug: 'test', description: '', specificationsJson: null, version: variant.version, isActive: true, variants: [variant], images: [] }
  expect(decodeProduct(product).variants).toHaveLength(1)
  expect(() => decodeProduct({ ...product, variants: [{ ...variant, productId: '3' }] })).toThrow()
  expect(() => decodeProduct({ ...product, images: [{ id: '1', variantId: '4', imageUrl: '/api/v1/catalog-images/' + 'a'.repeat(32) + '.png', altText: 'a', sortOrder: 0 }] })).toThrow()
})
test.each(['', '{}', '{"screen":"6.7 inch"}', '{"a":'.repeat(16) + '0' + '}'.repeat(16)])('accepts optional JSON object %s', text => expect(validateSpecifications(text)).toBe(true))
test.each(['[]', 'null', '123', '{broken', '{"a":'.repeat(17) + '0' + '}'.repeat(17)])('rejects invalid or deeply nested specifications %s', text => expect(validateSpecifications(text)).toBe(false))
