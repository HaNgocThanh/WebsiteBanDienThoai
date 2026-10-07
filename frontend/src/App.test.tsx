import { afterEach, expect, test, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import App from './App'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

test('health success displays the actual API message and blocks another click while pending', async () => {
  let complete!: (value: Response) => void
  const fetchMock = vi.fn(() => new Promise<Response>(resolve => { complete = resolve }))
  vi.stubGlobal('fetch', fetchMock)
  render(<App />)
  fireEvent.click(screen.getByRole('button', { name: 'Kiểm tra kết nối backend' }))
  expect((screen.getByRole('button') as HTMLButtonElement).disabled).toBe(true)
  complete(new Response(JSON.stringify({ status: 'ok', message: 'Real response fixture' }), { status: 200 }))
  expect(await screen.findByText('Real response fixture')).toBeTruthy()
  expect(fetchMock).toHaveBeenCalledWith('/api/health')
  expect((screen.getByRole('button') as HTMLButtonElement).disabled).toBe(false)
})

test('HTTP failure displays error and allows retry', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 503 })))
  render(<App />)
  fireEvent.click(screen.getByRole('button'))
  expect(await screen.findByText('Không kết nối được API. Hãy kiểm tra backend đang chạy.')).toBeTruthy()
  expect((screen.getByRole('button') as HTMLButtonElement).disabled).toBe(false)
})
