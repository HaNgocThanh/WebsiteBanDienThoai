import { useEffect, useRef, useState } from 'react'
import { ErrorNotice, SubmitButton } from '../components/primitives'
import { getApiHealth } from '../services/api'
import { ApiError } from '../services/errors'

export function HealthPage() {
  const [message, setMessage] = useState<string>()
  const [error, setError] = useState<ApiError>()
  const [loading, setLoading] = useState(false)
  const active = useRef<AbortController | null>(null)
  useEffect(() => () => active.current?.abort(), [])
  async function check() {
    if (active.current) return
    const controller = new AbortController()
    active.current = controller
    setLoading(true)
    setError(undefined)
    setMessage(undefined)
    try { const health = await getApiHealth(controller.signal); if (!controller.signal.aborted) setMessage(health.message) }
    catch (failure) { if (!controller.signal.aborted) setError(failure instanceof ApiError ? failure : new ApiError(0, 'NETWORK_ERROR')) }
    finally { if (active.current === controller) { active.current = null; if (!controller.signal.aborted) setLoading(false) } }
  }
  return <section className="page-section health-page"><span className="eyebrow">KẾT NỐI</span><h1>Trạng thái kết nối</h1><p className="muted">Kiểm tra khả năng kết nối đến PhoneStore.</p><div className="health-card"><div className="health-symbol" aria-hidden="true">↗</div><h2>Kết nối PhoneStore</h2><p className="muted">Chọn kiểm tra để nhận trạng thái hiện tại.</p><SubmitButton type="button" onClick={check} busy={loading}>{loading ? 'Đang kiểm tra…' : 'Kiểm tra kết nối'}</SubmitButton><div aria-live="polite" role="status">{loading ? 'Đang kết nối đến PhoneStore…' : message && <p className="notice notice-success">{message}</p>}</div>{error && <ErrorNotice error={error} />}</div></section>
}
