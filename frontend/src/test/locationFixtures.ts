// Synthetic fixtures for component tests; production uses the official API snapshot.
import { vi } from 'vitest'
import { locations } from '../services/locations'
export function mockLocations() {
  vi.spyOn(locations, 'provinces').mockResolvedValue({ asOf: '2026-10-09', items: [{ code: '79', name: 'Thành phố Hồ Chí Minh' }, { code: '48', name: 'Thành phố Đà Nẵng' }] })
  vi.spyOn(locations, 'wards').mockImplementation(async code => ({ asOf: '2026-10-09', items: [{ code: code === '79' ? '26734' : '20275', name: 'Phường Synthetic', provinceCode: code }] }))
}
