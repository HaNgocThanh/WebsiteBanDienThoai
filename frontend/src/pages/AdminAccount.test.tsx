import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, test, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AuthContext } from '../auth/AuthContext'
import { AdminLayout } from '../layouts/AdminLayout'
import { AdminAccountPage } from './AdminAccount'
import { AuthPage } from './AuthPages'
import * as authService from '../services/auth'
import { orderManagement } from '../services/orderManagement'
const user = { userId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', email: 'synthetic@example.invalid', fullName: 'Synthetic Admin', phone: null, emailConfirmed: true, roles: ['Customer', 'Admin'] }
afterEach(() => { cleanup(); vi.restoreAllMocks() })
test('Admin identity/menu excludes customer profile/own orders, uses shop feed and logout waits for server', async () => {
  const notices = vi.spyOn(orderManagement, 'notices').mockResolvedValue({ items: [], page: 1, pageSize: 1, totalCount: 0, unreadCount: 0 }); let finish!: () => void; const logout = vi.spyOn(authService, 'authCommand').mockImplementation(() => new Promise(resolve => { finish = resolve })); const clear = vi.fn()
  render(<AuthContext.Provider value={{ status: 'authenticated', user, clear, refresh: vi.fn() }}><MemoryRouter initialEntries={['/admin/account']}><Routes><Route path="/admin" element={<AdminLayout />}><Route path="account" element={<AdminAccountPage />} /></Route><Route path="/auth/login" element={<h1>Login destination</h1>} /></Routes></MemoryRouter></AuthContext.Provider>)
  await screen.findByRole('heading', { name: 'Tài khoản quản trị' }); expect(screen.queryByRole('link', { name: 'Đơn hàng của tôi' })).toBeNull(); expect(screen.queryByRole('heading', { name: 'Địa chỉ giao hàng' })).toBeNull(); await waitFor(() => expect(notices.mock.calls[0][2]).toBe(true))
  const button = screen.getByRole('button', { name: 'Đăng xuất' }); fireEvent.click(button); fireEvent.click(button); expect(logout).toHaveBeenCalledTimes(1); expect(clear).not.toHaveBeenCalled(); finish(); await screen.findByRole('heading', { name: 'Login destination' }); expect(clear).toHaveBeenCalledTimes(1)
})
test('Admin login defaults to management while explicit shop returnTo stays honored', async () => {
  vi.spyOn(authService, 'authCommand').mockResolvedValue(undefined)
  function open(entry: string) { render(<AuthContext.Provider value={{ status: 'anonymous', refresh: vi.fn().mockResolvedValue(user), clear: vi.fn() }}><MemoryRouter initialEntries={[entry]}><Routes><Route path="/auth/login" element={<AuthPage kind="login" />} /><Route path="/admin" element={<h1>Management destination</h1>} /><Route path="/account/orders" element={<h1>Personal destination</h1>} /></Routes></MemoryRouter></AuthContext.Provider>) }
  async function submit() { fireEvent.change(screen.getByLabelText('Email'), { target: { value: user.email } }); fireEvent.change(screen.getByLabelText('Mật khẩu'), { target: { value: 'Synthetic!Password123' } }); fireEvent.click(screen.getByRole('button', { name: 'Đăng nhập' })) }
  open('/auth/login'); await submit(); await screen.findByRole('heading', { name: 'Management destination' }); cleanup(); open('/auth/login?returnTo=/account/orders'); await submit(); await screen.findByRole('heading', { name: 'Personal destination' })
})
