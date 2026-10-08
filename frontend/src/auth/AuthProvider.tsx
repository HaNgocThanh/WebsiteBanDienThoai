import { useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { Link, Navigate, Outlet, useLocation } from 'react-router'
import { getSession } from '../services/auth'
import type { Session } from '../services/auth'
import { ApiError } from '../services/errors'
import { ErrorNotice, SubmitButton } from '../components/primitives'
import { AuthContext, useAuth } from './AuthContext'
import type { AuthState } from './AuthContext'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: 'loading' })
  const pending = useRef<AbortController | null>(null)
  async function refresh(): Promise<Session | null> {
    pending.current?.abort()
    const controller = new AbortController(); pending.current = controller
    try {
      const user = await getSession(controller.signal)
      if (!controller.signal.aborted) setState({ status: 'authenticated', user })
      return user
    } catch (error) {
      if (controller.signal.aborted) return null
      if (error instanceof ApiError && error.status === 401) { setState({ status: 'anonymous' }); return null }
      const failure = error instanceof ApiError ? error : new ApiError(0, 'NETWORK_ERROR')
      setState({ status: 'error', error: failure }); throw failure
    }
  }
  function clear() { pending.current?.abort(); setState({ status: 'anonymous' }) }
  useEffect(() => {
    let active = true
    queueMicrotask(() => { if (active) void refresh().catch(() => {}) })
    const focus = () => { void refresh().catch(() => {}) }
    window.addEventListener('focus', focus)
    window.addEventListener('auth-session-invalid', clear)
    return () => { active = false; pending.current?.abort(); window.removeEventListener('focus', focus); window.removeEventListener('auth-session-invalid', clear) }
  }, [])
  return <AuthContext.Provider value={{ ...state, refresh, clear }}>{children}</AuthContext.Provider>
}
export function AuthGuard({ admin = false }: { admin?: boolean }) {
  const auth = useAuth(); const location = useLocation()
  const panel = (content: ReactNode) => admin ? <main id="main-content" tabIndex={-1} className="store-main">{content}</main> : content
  if (auth.status === 'loading') return panel(<section className="page-section"><h1>Đang kiểm tra phiên</h1><p role="status">Vui lòng chờ…</p></section>)
  if (auth.status === 'error') return panel(<section className="page-section"><h1>Chưa thể kiểm tra phiên</h1><ErrorNotice error={auth.error!} /><SubmitButton onClick={() => { void auth.refresh().catch(() => {}) }}>Thử lại</SubmitButton></section>)
  if (auth.status === 'anonymous') return <Navigate to={`/auth/login?returnTo=${encodeURIComponent(location.pathname + location.search)}`} replace />
  if (admin && !auth.user?.roles.includes('Admin')) return panel(<section className="page-section"><h1>Bạn không có quyền quản trị</h1><p>Vui lòng dùng tài khoản được cấp quyền quản trị.</p><Link className="button" to="/account">Về tài khoản</Link></section>)
  return <Outlet />
}
