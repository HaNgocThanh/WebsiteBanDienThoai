import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, test, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AdminCustomerPage } from './AdminCustomerPage'
import { adminCustomers } from '../services/adminCustomers'
import * as membership from '../services/membership'
import { ApiError } from '../services/errors'
const profile = { userId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', email: 'synthetic@example.invalid', fullName: 'Synthetic customer', phone: null, isActive: true, emailConfirmed: true, canChangeStatus: true, createdAt: '2026-10-13T00:00:00Z', version: 'v1' }
afterEach(() => { cleanup(); vi.restoreAllMocks() })
function mount() {
  vi.spyOn(adminCustomers, 'addresses').mockResolvedValue([])
  vi.spyOn(membership, 'getMembership').mockResolvedValue({ enabled: true, tierCode: null, eligibleSpend: 0, nextTier: null, remainingSpend: 0, tiers: [] })
  vi.spyOn(membership, 'getSpend').mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0 }); vi.spyOn(membership, 'getHistory').mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0 })
  render(<MemoryRouter initialEntries={['/admin/customers/' + profile.userId]}><Routes><Route path="/admin/customers/:userId" element={<AdminCustomerPage />} /></Routes></MemoryRouter>)
}
test('Admin lost update response preserves exact key/version and draft while retry succeeds', async () => {
  vi.spyOn(adminCustomers, 'profile').mockResolvedValueOnce(profile).mockResolvedValue({ ...profile, fullName: 'Updated customer', version: 'v2' })
  const update = vi.spyOn(adminCustomers, 'update').mockRejectedValueOnce(new ApiError(0, 'TIMEOUT')).mockResolvedValue({ version: 'v2' })
  mount(); const input = await screen.findByLabelText('Họ tên khách hàng'); fireEvent.change(input, { target: { value: 'Updated customer' } }); const button = screen.getByRole('button', { name: 'Lưu thông tin khách hàng' }); fireEvent.click(button); fireEvent.click(button); await screen.findByRole('alert')
  expect(update).toHaveBeenCalledTimes(1); expect((input.closest('fieldset') as HTMLFieldSetElement).disabled).toBe(true); fireEvent.click(screen.getByRole('button', { name: 'Thử cập nhật lại' })); await screen.findByText('Đã cập nhật thông tin khách hàng.'); expect(update.mock.calls[0].slice(0, 2)).toEqual(update.mock.calls[1].slice(0, 2)); expect((screen.getByLabelText('Email khách hàng') as HTMLInputElement).readOnly).toBe(true)
})
test('stale profile keeps draft and protected Admin status cannot be changed', async () => {
  vi.spyOn(adminCustomers, 'profile').mockResolvedValue({ ...profile, canChangeStatus: false }); vi.spyOn(adminCustomers, 'update').mockRejectedValue(new ApiError(412, 'VERSION_CONFLICT'))
  mount(); const input = await screen.findByLabelText('Họ tên khách hàng'); expect((screen.getByLabelText('Tài khoản hoạt động') as HTMLInputElement).disabled).toBe(true); fireEvent.change(input, { target: { value: 'Preserved draft' } }); fireEvent.click(screen.getByRole('button', { name: 'Lưu thông tin khách hàng' })); await screen.findByRole('alert'); expect((input as HTMLInputElement).value).toBe('Preserved draft'); await waitFor(() => expect(screen.queryByText('Đã cập nhật thông tin khách hàng.')).toBeNull()); expect(screen.getByRole('link', { name: 'Xem đơn hàng của khách' }).getAttribute('href')).toContain(profile.userId)
})
