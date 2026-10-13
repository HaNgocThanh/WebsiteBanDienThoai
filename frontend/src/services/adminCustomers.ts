import { request } from './client'
import { isRecord } from './errors'
import { decodeAddress } from './profile'
export interface AdminCustomerProfile { userId: string; email: string; fullName: string; phone: string | null; isActive: boolean; emailConfirmed: boolean; canChangeStatus: boolean; createdAt: string; version: string }
export interface CustomerUpdate { fullName: string; phone: string | null; isActive: boolean; version: string; operationKey: string }
const path = (user: string) => '/api/v1/admin/customers/' + encodeURIComponent(user)
export function decodeCustomerProfile(v: unknown): AdminCustomerProfile {
  if (!isRecord(v) || typeof v.userId !== 'string' || typeof v.email !== 'string' || typeof v.fullName !== 'string' || !(v.phone === null || typeof v.phone === 'string') || typeof v.isActive !== 'boolean' || typeof v.emailConfirmed !== 'boolean' || typeof v.canChangeStatus !== 'boolean' || typeof v.version !== 'string' || !v.version || typeof v.createdAt !== 'string' || !Number.isFinite(Date.parse(v.createdAt))) throw new TypeError('Invalid customer profile')
  return v as unknown as AdminCustomerProfile
}
export const adminCustomers = {
  profile: (user: string, signal?: AbortSignal) => request(path(user) + '/profile', decodeCustomerProfile, { signal }),
  addresses: (user: string, signal?: AbortSignal) => request(path(user) + '/addresses', v => { if (!Array.isArray(v)) throw new TypeError('Invalid addresses'); return v.map(decodeAddress) }, { signal }),
  update: (user: string, body: CustomerUpdate, signal?: AbortSignal) => request(path(user) + '/profile', v => { if (!isRecord(v) || typeof v.version !== 'string' || !v.version) throw new TypeError('Invalid receipt'); return { version: v.version } }, { method: 'PUT', body, signal }),
}
