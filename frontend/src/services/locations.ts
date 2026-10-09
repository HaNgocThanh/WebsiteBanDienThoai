import { request } from './client'
import { isRecord } from './errors'

export interface Location { code: string; name: string; provinceCode?: string }
export interface Destination { addressLine: string; province: string; locality: string; provinceCode: string; wardCode: string }
export const emptyDestination: Destination = { addressLine: '', province: '', locality: '', provinceCode: '', wardCode: '' }
export interface LocationList { asOf: string; items: Location[] }
export function decodeLocations(value: unknown, provinceCode?: string): LocationList {
  if (!isRecord(value) || typeof value.asOf !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value.asOf) || !Array.isArray(value.items) || !value.items.length) throw new TypeError('Invalid location list')
  const items = value.items.map(item => {
    if (!isRecord(item) || typeof item.code !== 'string' || !(provinceCode ? /^\d{5}$/ : /^\d{2}$/).test(item.code) || typeof item.name !== 'string' || !item.name.trim() || (provinceCode && item.provinceCode !== provinceCode)) throw new TypeError('Invalid location')
    return { code: item.code, name: item.name, ...(provinceCode ? { provinceCode } : {}) }
  })
  if (new Set(items.map(x => x.code)).size !== items.length) throw new TypeError('Duplicate locations')
  items.sort((a, b) => a.name.replace(/^(Tỉnh|Thành phố|Phường|Xã|Đặc khu) /, '').localeCompare(b.name.replace(/^(Tỉnh|Thành phố|Phường|Xã|Đặc khu) /, ''), 'vi-VN'))
  return { asOf: value.asOf, items }
}
export const locations = {
  provinces: (signal?: AbortSignal) => request('/api/v1/locations/provinces', value => decodeLocations(value), { signal }),
  wards: (code: string, signal?: AbortSignal) => request(`/api/v1/locations/provinces/${encodeURIComponent(code)}/wards`, value => decodeLocations(value, code), { signal }),
}
