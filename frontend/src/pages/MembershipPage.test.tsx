import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, expect, test, vi } from 'vitest'
import { MemoryRouter } from 'react-router'
import * as api from '../services/membership'
import { MembershipPage } from './MembershipPage'
const config: api.Configuration = { enabled: true, tiers: ['Bronze', 'Silver', 'Gold', 'Diamond'].map((code, i) => ({ code, name: api.tierNames[code], minimumSpend: [1000000, 10000000, 50000000, 100000000][i], version: 'AAAAAAAAAAE=' })) }
afterEach(() => { cleanup(); vi.restoreAllMocks() })
test('unranked customer sees remaining spend and negative refund with safe owned order link', async () => {
  vi.spyOn(api, 'getMembership').mockResolvedValue({ ...config, tierCode: null, eligibleSpend: 999999, nextTier: config.tiers[0], remainingSpend: 1 })
  vi.spyOn(api, 'getSpend').mockResolvedValue({ items: [{ id: '3', orderId: '9007199254740993', refundId: '4', kind: 'Refund', amount: -1, createdAt: '2026-10-12T00:00:00Z' }], page: 1, pageSize: 20, totalCount: 1 })
  vi.spyOn(api, 'getHistory').mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0 })
  render(<MemoryRouter><MembershipPage /></MemoryRouter>); await screen.findByRole('heading', { name: 'Chưa có hạng' }); expect(screen.getByRole('link', { name: 'Đơn #9007199254740993' }).getAttribute('href')).toBe('/account/orders/9007199254740993'); expect(screen.getByText(/Còn 1/)).toBeTruthy(); expect(screen.getByText('Chưa có thay đổi hạng.')).toBeTruthy()
})
test('decoder rejects unsafe money, invalid code and duplicated policy rows', () => {
  expect(() => api.decodeConfiguration({ ...config, tiers: [config.tiers[0], config.tiers[0], ...config.tiers.slice(2)] })).toThrow()
  expect(() => api.decodeTier({ ...config.tiers[0], minimumSpend: Number.MAX_SAFE_INTEGER + 1 })).toThrow()
  expect(() => api.decodeTier({ ...config.tiers[0], code: 'Admin' })).toThrow()
})
