import { useState } from 'react'
import { getApiHealth } from './services/api'

export default function App() {
  const [message, setMessage] = useState('Chưa kiểm tra kết nối')
  const [loading, setLoading] = useState(false)

  async function checkConnection() {
    setLoading(true)

    try {
      const result = await getApiHealth()
      setMessage(result.message)
    } catch {
      setMessage('Không kết nối được API. Hãy kiểm tra backend đang chạy.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <main>
      <h1>PhoneStore</h1>
      <p>Website bán điện thoại</p>

      <button onClick={checkConnection} disabled={loading}>
        {loading ? 'Đang kiểm tra…' : 'Kiểm tra kết nối backend'}
      </button>

      <p>{message}</p>
    </main>
  )
}