import { expect, test } from 'vitest'
import { decodeAddress, decodeProfile } from './profile'

const profile = { userId: 'test-id', email: 'test@example.invalid', fullName: 'Test', phone: null, tierCode: 'Bronze', eligibleSpend: 0 }
const address = { id: '9223372036854775807', recipientName: 'Test', phone: '0', addressLine: 'Test street', locality: null, province: 'Test', countryCode: 'VN', isDefault: true }
test('profile rejects unsafe or fractional monetary values', () => {
  expect(decodeProfile(profile)).toEqual(profile)
  for (const eligibleSpend of [-1, 0.5, Number.MAX_SAFE_INTEGER + 1]) expect(() => decodeProfile({ ...profile, eligibleSpend })).toThrow()
})
test('address preserves SQL bigint as string and rejects invalid ids and default values', () => {
  expect(decodeAddress(address).id).toBe('9223372036854775807')
  for (const id of [1, '0', '01', '9223372036854775808']) expect(() => decodeAddress({ ...address, id })).toThrow()
  expect(() => decodeAddress({ ...address, isDefault: 'true' })).toThrow()
})
