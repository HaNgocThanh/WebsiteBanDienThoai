import { decodeEmpty, request } from './client'
import { isRecord } from './errors'

export interface Profile { userId: string; email: string; fullName: string; phone: string | null; tierCode: string | null; eligibleSpend: number }
export interface AddressInput { recipientName: string; phone: string; addressLine: string; locality: string | null; province: string; countryCode: string; isDefault: boolean }
export interface Address extends AddressInput { id: string }
export function decodeProfile(value: unknown): Profile {
  if (!isRecord(value) || typeof value.userId !== 'string' || typeof value.email !== 'string' || typeof value.fullName !== 'string'
    || !(value.phone === null || typeof value.phone === 'string') || !(value.tierCode === null || typeof value.tierCode === 'string')
    || typeof value.eligibleSpend !== 'number' || !Number.isSafeInteger(value.eligibleSpend) || value.eligibleSpend < 0) throw new TypeError('Invalid profile')
  return value as unknown as Profile
}
export function decodeAddress(value: unknown): Address {
  if (!isRecord(value) || typeof value.id !== 'string' || !/^[1-9]\d*$/.test(value.id) || BigInt(value.id) > 9223372036854775807n
    || !['recipientName', 'phone', 'addressLine', 'province', 'countryCode'].every(key => typeof value[key] === 'string')
    || !(value.locality === null || typeof value.locality === 'string') || typeof value.isDefault !== 'boolean') throw new TypeError('Invalid address')
  return value as unknown as Address
}
export const getProfile = (signal?: AbortSignal) => request('/api/v1/me', decodeProfile, { signal })
export const updateProfile = (body: { fullName: string; phone: string | null }, signal?: AbortSignal) => request('/api/v1/me', decodeEmpty, { method: 'PATCH', body, signal })
export const getAddresses = (signal?: AbortSignal) => request('/api/v1/me/addresses', value => {
  if (!Array.isArray(value)) throw new TypeError('Invalid addresses')
  return value.map(decodeAddress)
}, { signal })
export const saveAddress = (id: string | undefined, body: AddressInput, signal?: AbortSignal) => request('/api/v1/me/addresses' + (id ? '/' + encodeURIComponent(id) : ''), decodeAddress, { method: id ? 'PUT' : 'POST', body, signal })
export const deleteAddress = (id: string, signal?: AbortSignal) => request('/api/v1/me/addresses/' + encodeURIComponent(id), decodeEmpty, { method: 'DELETE', signal })
