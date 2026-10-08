import { decodeEmpty, request } from './client'
import { isRecord } from './errors'

export interface Session { userId: string; email: string; fullName: string; phone: string | null; emailConfirmed: boolean; roles: string[] }
export function decodeSession(value: unknown): Session {
  if (!isRecord(value) || typeof value.userId !== 'string' || !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value.userId)
    || typeof value.email !== 'string' || typeof value.fullName !== 'string' || !(value.phone === null || typeof value.phone === 'string')
    || value.emailConfirmed !== true || !Array.isArray(value.roles) || !value.roles.every(role => typeof role === 'string')) throw new TypeError('Invalid session')
  return { userId: value.userId, email: value.email, fullName: value.fullName, phone: value.phone, emailConfirmed: true, roles: value.roles }
}
export const getSession = (signal?: AbortSignal) => request('/api/v1/auth/session', decodeSession, { signal })
export const authCommand = (action: 'register' | 'login' | 'logout' | 'verify-email' | 'forgot-password' | 'reset-password' | 'resend-verification', body: unknown, signal?: AbortSignal) => request(`/api/v1/auth/${action}`, decodeEmpty, { method: 'POST', body, signal })
export function safeReturnPath(value: string | null): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.includes('\\') || [...value].some(char => char.charCodeAt(0) < 32)) return '/account'
  const url = new URL(value, 'https://return.invalid')
  if (url.origin !== 'https://return.invalid' || url.pathname.startsWith('/auth/') || url.hash) return '/account'
  return url.pathname + url.search
}
