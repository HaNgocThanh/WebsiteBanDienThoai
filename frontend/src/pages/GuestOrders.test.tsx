import { StrictMode } from 'react'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, test, vi } from 'vitest'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { AuthContext } from '../auth/AuthContext'
import { forgetGuestLink } from '../checkout/guestLink'
import { orders } from '../services/orders'
import { GuestAccessPage, GuestLookupPage } from './GuestOrders'

afterEach(() => { cleanup(); forgetGuestLink(); vi.restoreAllMocks() })
function Location() { const location = useLocation(); return <div data-testid="hash">{location.hash ? 'present' : 'removed'}</div> }
test('StrictMode removes fragment immediately and consumes token exactly once on explicit action', async () => {
  const exchange = vi.spyOn(orders, 'exchange').mockResolvedValue({ purpose: 'ViewOrder', expiresAt: '2026-10-09T10:30:00Z' })
  render(<StrictMode><AuthContext.Provider value={{ status: 'anonymous', refresh: vi.fn(), clear: vi.fn() }}><MemoryRouter initialEntries={['/guest/access#token=' + 'a'.repeat(64) + '&purpose=ViewOrder']}><Location /><Routes><Route path="/guest/access" element={<GuestAccessPage />} /><Route path="/guest/order" element={<p>Scoped order</p>} /></Routes></MemoryRouter></AuthContext.Provider></StrictMode>)
  await waitFor(() => expect(screen.getByTestId('hash').textContent).toBe('removed')); expect(exchange).not.toHaveBeenCalled(); fireEvent.click(screen.getByRole('button', { name: 'Xác thực và xem đơn' })); fireEvent.click(screen.getByRole('button', { name: 'Xác thực và xem đơn' })); await screen.findByText('Scoped order'); expect(exchange).toHaveBeenCalledTimes(1); expect(sessionStorage.length).toBe(0); expect(localStorage.length).toBe(0)
})
test('claim link stays in memory for auth and never enters returnTo URL or exchange API', async () => {
  const claim = vi.spyOn(orders, 'claim'), exchange = vi.spyOn(orders, 'exchange')
  render(<AuthContext.Provider value={{ status: 'anonymous', refresh: vi.fn(), clear: vi.fn() }}><MemoryRouter initialEntries={['/guest/access#token=' + 'b'.repeat(64) + '&purpose=ClaimOrder']}><GuestAccessPage /></MemoryRouter></AuthContext.Provider>)
  expect(screen.getByRole('link', { name: 'Đăng nhập để nhận đơn' }).getAttribute('href')).toBe('/auth/login?returnTo=%2Fguest%2Faccess'); expect(claim).not.toHaveBeenCalled(); expect(exchange).not.toHaveBeenCalled(); expect(sessionStorage.length).toBe(0)
})
test('lookup success is neutral even when request has unknown order', async () => {
  const request = vi.spyOn(orders, 'requestAccess').mockResolvedValue()
  render(<MemoryRouter><GuestLookupPage /></MemoryRouter>); fireEvent.change(screen.getByLabelText('Mã đơn hàng'), { target: { value: 'missing' } }); fireEvent.change(screen.getByLabelText('Email đặt hàng'), { target: { value: 'synthetic@example.invalid' } }); fireEvent.click(screen.getByRole('button', { name: 'Gửi liên kết qua email' })); await screen.findByRole('status'); expect(screen.getByRole('status').textContent).toContain('Nếu thông tin phù hợp'); expect(request).toHaveBeenCalledWith('missing', 'synthetic@example.invalid', 'ViewOrder'); expect(sessionStorage.length).toBe(0)
})
