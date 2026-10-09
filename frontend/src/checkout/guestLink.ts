// Lives only during SPA navigation (including login/register); a full reload intentionally discards it.
export interface GuestLink { token: string; purpose: 'ViewOrder' | 'CancelOrder' | 'ClaimOrder' }
let memory: GuestLink | undefined
export function captureGuestLink(hash: string): GuestLink | undefined {
  if (!hash) return memory
  const values = new URLSearchParams(hash.slice(1)), token = values.get('token'), purpose = values.get('purpose')
  memory = token && /^[0-9a-f]{64}$/.test(token) && (purpose === 'ViewOrder' || purpose === 'CancelOrder' || purpose === 'ClaimOrder') ? { token, purpose } : undefined
  return memory
}
export function forgetGuestLink() { memory = undefined }
