import { emptyDestination } from '../services/locations'
import { useState } from 'react'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, test, vi } from 'vitest'
import { AddressFields } from './AddressFields'
import { QuantityField } from './QuantityField'
import { locations, decodeLocations } from '../services/locations'
import { mockLocations } from '../test/locationFixtures'
import { ApiError } from '../services/errors'

afterEach(() => { cleanup(); vi.restoreAllMocks() })
function Address() { const [value, setValue] = useState(emptyDestination); return <><AddressFields value={value} onChange={setValue} /><output>{JSON.stringify(value)}</output></> }
test('wards appear only after province selection and are cleared on province change', async () => {
  mockLocations(); render(<Address />); expect(screen.queryByLabelText('Phường/xã')).toBeNull()
  await screen.findByRole('option', { name: 'Thành phố Hồ Chí Minh' })
  fireEvent.change(screen.getByLabelText('Tỉnh/thành phố'), { target: { value: '79' } }); await screen.findByRole('option', { name: 'Phường Synthetic' })
  fireEvent.change(screen.getByLabelText('Phường/xã'), { target: { value: '26734' } }); expect(screen.getByRole('status').textContent).toContain('26734')
  fireEvent.change(screen.getByLabelText('Tỉnh/thành phố'), { target: { value: '48' } }); expect(screen.getByRole('status').textContent).toContain('"wardCode":""')
  await waitFor(() => expect(locations.wards).toHaveBeenLastCalledWith('48', expect.any(AbortSignal)))
})
test('catalog failures have retry without pretending an address is available', async () => {
  vi.spyOn(locations, 'provinces').mockRejectedValueOnce(new ApiError(0, 'NETWORK_ERROR')).mockResolvedValueOnce({ asOf: '2026-10-09', items: [{ code: '79', name: 'Thành phố Hồ Chí Minh' }] })
  render(<Address />); await screen.findByRole('alert'); expect((screen.getByLabelText('Tỉnh/thành phố') as HTMLSelectElement).disabled).toBe(true)
  fireEvent.click(screen.getByRole('button', { name: 'Tải lại danh mục địa chỉ' })); await screen.findByRole('option', { name: 'Thành phố Hồ Chí Minh' }); expect(screen.queryByRole('alert')).toBeNull()
})
test('location boundary sorts geographic names A–Z and rejects cross-province wards/duplicates', () => {
  expect(decodeLocations({ asOf: '2026-10-09', items: [{ code: '79', name: 'Thành phố Hồ Chí Minh' }, { code: '96', name: 'Tỉnh Cà Mau' }] }).items.map(x => x.code)).toEqual(['96', '79'])
  expect(() => decodeLocations({ asOf: '2026-10-09', items: [{ code: '12345', name: 'Xã Test', provinceCode: '48' }] }, '79')).toThrow()
  expect(() => decodeLocations({ asOf: '2026-10-09', items: [{ code: '79', name: 'Test' }, { code: '79', name: 'Test' }] })).toThrow()
})
function Quantity() { const [value, setValue] = useState('1'); return <QuantityField label="Số lượng" value={value} onChange={setValue} max={3} /> }
test('quantity supports right buttons, keyboard arrows, typed value and bounds', () => {
  render(<Quantity />); const input = screen.getByRole('spinbutton')
  expect((screen.getByRole('button', { name: 'Giảm số lượng' }) as HTMLButtonElement).disabled).toBe(true)
  fireEvent.click(screen.getByRole('button', { name: 'Tăng số lượng' })); expect((input as HTMLInputElement).value).toBe('2'); expect(document.activeElement).toBe(input)
  fireEvent.keyDown(input, { key: 'ArrowUp' }); expect((input as HTMLInputElement).value).toBe('3')
  fireEvent.keyDown(input, { key: 'ArrowUp' }); expect((input as HTMLInputElement).value).toBe('3')
  fireEvent.keyDown(input, { key: 'ArrowDown' }); expect((input as HTMLInputElement).value).toBe('2')
  fireEvent.change(input, { target: { value: 'invalid' } }); fireEvent.keyDown(input, { key: 'ArrowUp' }); expect((input as HTMLInputElement).value).toBe('1')
})
