import { request } from './client'
import { isRecord } from './errors'
export const tierNames: Record<string, string> = { Bronze: 'Đồng', Silver: 'Bạc', Gold: 'Vàng', Diamond: 'Kim cương' }
export interface Tier { code: string; name: string; minimumSpend: number; version: string | null; id?: string | null }
export interface Configuration { enabled: boolean; tiers: Tier[] }
export interface Membership extends Configuration { tierCode: string | null; eligibleSpend: number; nextTier: Tier | null; remainingSpend: number }
export interface Spend { id: string; orderId: string; refundId: string | null; kind: string; amount: number; createdAt: string }
export interface History { id: string; previousTierCode: string | null; newTierCode: string | null; eligibleSpend: number; reason: string; createdAt: string }
export interface Page<T> { items: T[]; page: number; pageSize: number; totalCount: number }
const money = (v: unknown): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= 0
const id = (v: unknown): v is string => typeof v === 'string' && /^[1-9]\d*$/.test(v) && BigInt(v) <= 9223372036854775807n
const code = (v: unknown): v is string => typeof v === 'string' && Object.hasOwn(tierNames, v)
export function decodeTier(v: unknown): Tier {
  if (!isRecord(v) || !code(v.code) || typeof v.name !== 'string' || !money(v.minimumSpend) || !(v.id === undefined || v.id === null || id(v.id)) || !(v.version === null || typeof v.version === 'string' && /^[A-Za-z0-9+/]{11}=$/.test(v.version))) throw new TypeError('Invalid tier')
  return v as unknown as Tier
}
export function decodeConfiguration(v: unknown): Configuration {
  if (!isRecord(v) || typeof v.enabled !== 'boolean' || !Array.isArray(v.tiers)) throw new TypeError('Invalid configuration')
  const tiers = v.tiers.map(decodeTier)
  if (new Set(tiers.map(t => t.code)).size !== tiers.length || v.enabled && (tiers.length !== 4 || tiers.some((t, i) => t.minimumSpend <= 0 || i > 0 && t.minimumSpend <= tiers[i - 1].minimumSpend))) throw new TypeError('Invalid policy')
  return { enabled: v.enabled, tiers }
}
export function decodeMembership(v: unknown): Membership {
  const config = decodeConfiguration(v)
  if (!isRecord(v) || !(v.tierCode === null || code(v.tierCode)) || !money(v.eligibleSpend) || !money(v.remainingSpend)) throw new TypeError('Invalid membership')
  return { ...config, tierCode: v.tierCode, eligibleSpend: v.eligibleSpend, remainingSpend: v.remainingSpend, nextTier: v.nextTier === null ? null : decodeTier(v.nextTier) }
}
function decodePage<T>(v: unknown, decode: (item: unknown) => T): Page<T> {
  if (!isRecord(v) || !Array.isArray(v.items) || !money(v.page) || v.page < 1 || !money(v.pageSize) || v.pageSize < 1 || v.pageSize > 100 || !money(v.totalCount)) throw new TypeError('Invalid page')
  return { items: v.items.map(decode), page: v.page, pageSize: v.pageSize, totalCount: v.totalCount }
}
function decodeSpend(v: unknown): Spend {
  if (!isRecord(v) || !id(v.id) || !id(v.orderId) || !(v.refundId === null || id(v.refundId)) || !['OrderCompleted', 'Refund'].includes(String(v.kind)) || typeof v.amount !== 'number' || !Number.isSafeInteger(v.amount) || typeof v.createdAt !== 'string' || !Number.isFinite(Date.parse(v.createdAt)) || (v.kind === 'Refund' ? v.amount >= 0 || v.refundId === null : v.amount < 0 || v.refundId !== null)) throw new TypeError('Invalid spend')
  return v as unknown as Spend
}
function decodeHistory(v: unknown): History {
  if (!isRecord(v) || !id(v.id) || !(v.previousTierCode === null || code(v.previousTierCode)) || !(v.newTierCode === null || code(v.newTierCode)) || !money(v.eligibleSpend) || typeof v.reason !== 'string' || typeof v.createdAt !== 'string' || !Number.isFinite(Date.parse(v.createdAt))) throw new TypeError('Invalid history')
  return v as unknown as History
}
const customerPath = (user?: string) => user ? '/api/v1/admin/customers/' + encodeURIComponent(user) : '/api/v1/me/membership'
export const getMembership = (signal?: AbortSignal, user?: string) => request(customerPath(user), decodeMembership, { signal })
export const getConfiguration = (admin = false, signal?: AbortSignal) => request(admin ? '/api/v1/admin/customer-tiers' : '/api/v1/membership/tiers', decodeConfiguration, { signal })
export const getSpend = (page: number, signal?: AbortSignal, user?: string) => request(`${customerPath(user)}/spend?page=${page}`, v => decodePage(v, decodeSpend), { signal })
export const getHistory = (page: number, signal?: AbortSignal, user?: string) => request(`${customerPath(user)}/history?page=${page}`, v => decodePage(v, decodeHistory), { signal })
export interface Customer { userId: string; fullName: string; email: string; tierCode: string | null; eligibleSpend: number; phone: string | null; isActive: boolean; emailConfirmed: boolean }
export const getCustomers = (query: URLSearchParams, signal?: AbortSignal) => request('/api/v1/admin/customers?' + query, v => decodePage(v, item => {
  if (!isRecord(item) || typeof item.userId !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(item.userId) || typeof item.fullName !== 'string' || typeof item.email !== 'string' || !(item.tierCode === null || code(item.tierCode)) || !money(item.eligibleSpend) || !(item.phone === null || typeof item.phone === 'string') || typeof item.isActive !== 'boolean' || typeof item.emailConfirmed !== 'boolean') throw new TypeError('Invalid customer')
  return item as unknown as Customer
}), { signal })
