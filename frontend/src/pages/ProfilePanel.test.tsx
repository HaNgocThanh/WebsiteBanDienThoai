import { afterEach, expect, test, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { AuthContext } from '../auth/AuthContext'
import { ApiError } from '../services/errors'
import { ProfilePanel } from './ProfilePanel'
import { deleteAddress, saveAddress, updateProfile } from '../services/profile'

vi.mock('../services/profile', () => ({
  getProfile: vi.fn(async () => ({ userId: 'test', email: 'test@example.invalid', fullName: 'Test', phone: null, tierCode: 'Bronze', eligibleSpend: 0 })),
  getAddresses: vi.fn(async () => [{ id: '1', recipientName: 'Original', phone: '0', addressLine: 'Synthetic', locality: null, province: 'Test', countryCode: 'VN', isDefault: true }]),
  updateProfile: vi.fn(), saveAddress: vi.fn(), deleteAddress: vi.fn(),
}))
afterEach(() => { cleanup(); vi.clearAllMocks() })
function mount() { render(<AuthContext.Provider value={{ status: 'authenticated', refresh: vi.fn(async () => null), clear: vi.fn() }}><ProfilePanel /></AuthContext.Provider>) }

test('profile pending submit is locked and failure preserves input without success', async () => {
  let reject!: (failure: unknown) => void
  vi.mocked(updateProfile).mockImplementation(() => new Promise((_, fail) => { reject = fail }))
  mount(); const email = await screen.findByLabelText('Email tài khoản') as HTMLInputElement
  expect(email.readOnly).toBe(true)
  fireEvent.change(screen.getByLabelText('Họ tên'), { target: { value: 'Changed' } })
  const button = screen.getByRole('button', { name: 'Lưu hồ sơ' })
  fireEvent.click(button); fireEvent.click(button)
  expect(updateProfile).toHaveBeenCalledTimes(1)
  expect(updateProfile).toHaveBeenCalledWith({ fullName: 'Changed', phone: null }, expect.any(AbortSignal))
  reject(new ApiError(403, 'CSRF_INVALID'))
  await screen.findByRole('alert')
  expect((screen.getByLabelText('Họ tên') as HTMLInputElement).value).toBe('Changed')
  expect(screen.queryByText('Hồ sơ đã được cập nhật.')).toBeNull()
})

test('failed address save or delete preserves saved address and default', async () => {
  vi.mocked(saveAddress).mockRejectedValue(new ApiError(400, 'VALIDATION_ERROR'))
  vi.mocked(deleteAddress).mockRejectedValue(new ApiError(404, 'NOT_FOUND'))
  mount(); await screen.findByRole('button', { name: 'Sửa Original' })
  fireEvent.click(screen.getByRole('button', { name: 'Sửa Original' }))
  fireEvent.change(screen.getByLabelText('Người nhận'), { target: { value: 'Rejected' } })
  fireEvent.submit(screen.getByRole('button', { name: 'Lưu địa chỉ' }).closest('form')!)
  await screen.findByRole('alert')
  expect(screen.getByRole('button', { name: 'Sửa Original' })).toBeTruthy()
  expect(screen.getByText('· Mặc định')).toBeTruthy()
  expect(screen.queryByText('Địa chỉ đã được lưu.')).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Xóa Original' }))
  fireEvent.click(screen.getByRole('button', { name: 'Xác nhận xóa' }))
  await waitFor(() => expect(deleteAddress).toHaveBeenCalledTimes(1))
  await screen.findByRole('alert')
  expect(screen.getByRole('button', { name: 'Sửa Original' })).toBeTruthy()
  expect(screen.queryByText('Địa chỉ đã được xóa.')).toBeNull()
})
