import { expect, test } from 'vitest'
import { validCatalogImage } from './catalogImageFile'
test.each([['jpg', 'image/jpeg'], ['jpeg', 'image/jpeg'], ['png', 'image/png'], ['webp', 'image/webp'], ['gif', 'image/gif'], ['bmp', 'image/bmp'], ['svg', 'image/svg+xml']])('upload picker accepts %s without trusting mismatched MIME', (extension, mime) => {
  expect(validCatalogImage(new File(['synthetic'], `image.${extension}`, { type: mime }))).toBe(true)
  expect(validCatalogImage(new File(['synthetic'], `image.${extension}`, { type: 'text/plain' }))).toBe(false)
})
test('picker rejects missing, empty, oversized and unsupported files', () => {
  expect(validCatalogImage(undefined)).toBe(false)
  expect(validCatalogImage(new File([], 'empty.png', { type: 'image/png' }))).toBe(false)
  expect(validCatalogImage(new File([new Uint8Array(10 * 1024 * 1024 + 1)], 'large.png', { type: 'image/png' }))).toBe(false)
  expect(validCatalogImage(new File(['synthetic'], 'file.exe', { type: 'image/png' }))).toBe(false)
})
