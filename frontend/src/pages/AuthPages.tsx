import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router'
import { useAuth } from '../auth/AuthContext'
import { ErrorNotice, SubmitButton, TextField } from '../components/primitives'
import { authCommand, safeReturnPath } from '../services/auth'
import { ApiError, fieldError } from '../services/errors'
import { ProfilePanel } from './ProfilePanel'

type Kind = 'login' | 'register' | 'forgot-password' | 'resend-verification' | 'verify-email' | 'reset-password'
const titles: Record<Kind, string> = { login: 'Đăng nhập', register: 'Tạo tài khoản', 'forgot-password': 'Quên mật khẩu', 'resend-verification': 'Gửi lại email xác minh', 'verify-email': 'Xác minh email', 'reset-password': 'Đặt lại mật khẩu' }
const neutral = 'Nếu thông tin phù hợp, bạn sẽ nhận được email hướng dẫn. Hãy kiểm tra hộp thư và thư rác; có thể yêu cầu gửi lại nếu chưa nhận được.'

export function AuthPage({ kind }: { kind: Kind }) {
  const [email, setEmail] = useState(''); const [password, setPassword] = useState(''); const [confirmation, setConfirmation] = useState('')
  const [fullName, setFullName] = useState(''); const [phone, setPhone] = useState('')
  const [error, setError] = useState<ApiError>(); const [validation, setValidation] = useState<Record<string, string[]>>({})
  const [busy, setBusy] = useState(false); const [success, setSuccess] = useState('')
  const lock = useRef(false); const controller = useRef<AbortController | null>(null)
  const linkGeneration = useRef(0)
  const auth = useAuth(); const navigate = useNavigate(); const location = useLocation(); const [search] = useSearchParams()
  const needsLink = kind === 'verify-email' || kind === 'reset-password'
  const [link, setLink] = useState(() => { const values = new URLSearchParams(location.hash.slice(1)); return { version: 0, source: location.hash, userId: values.get('userId') ?? '', token: values.get('token') ?? '' } })
  // Same-document fragment navigation can open a second email link without a remount.
  // Track the previous fragment; scrubbing it keeps the captured token in memory.
  if (needsLink && location.hash !== link.source) {
    if (location.hash) {
      const values = new URLSearchParams(location.hash.slice(1))
      setLink({ version: link.version + 1, source: location.hash, userId: values.get('userId') ?? '', token: values.get('token') ?? '' })
      setSuccess(''); setError(undefined); setPassword(''); setConfirmation(''); setValidation({}); setBusy(false)
    } else setLink({ ...link, source: '' })
  }
  const validLink = /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(link.userId) && link.token.length > 0 && link.token.length <= 4096
  const hasEmail = ['login', 'register', 'forgot-password', 'resend-verification'].includes(kind)
  const hasPassword = ['login', 'register', 'reset-password'].includes(kind)
  const newPassword = kind === 'register' || kind === 'reset-password'
  useEffect(() => {
    linkGeneration.current = link.version
    if (link.version > 0) { controller.current?.abort(); lock.current = false }
  }, [link.version])
  useEffect(() => {
    if (needsLink && location.hash) navigate({ pathname: location.pathname, search: location.search }, { replace: true })
  }, [needsLink, location.hash, location.pathname, location.search, navigate])
  useEffect(() => () => { controller.current?.abort() }, [])
  function field(name: string) { return fieldError(validation, name) ?? fieldError(error?.errors ?? {}, name) }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (lock.current) return
    setSuccess(''); setError(undefined)
    const errors: Record<string, string[]> = {}
    if (hasEmail && (!email.trim() || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim()))) errors.email = ['Vui lòng nhập email hợp lệ.']
    if (hasPassword && !password) errors.password = ['Vui lòng nhập mật khẩu.']
    if (newPassword && (password.length < 12 || !/[A-Z]/.test(password) || !/[a-z]/.test(password) || !/[0-9]/.test(password) || !/[^A-Za-z0-9]/.test(password))) errors.password = ['Mật khẩu cần ít nhất 12 ký tự, có chữ hoa, chữ thường, số và ký tự đặc biệt.']
    if (newPassword && password !== confirmation) errors.confirmation = ['Mật khẩu nhập lại chưa khớp.']
    if (kind === 'register' && !fullName.trim()) errors.fullName = ['Vui lòng nhập họ tên.']
    setValidation(errors)
    if (Object.keys(errors).length) { requestAnimationFrame(() => document.querySelector<HTMLInputElement>('.auth-form input[aria-invalid="true"]')?.focus()); return }
    if (needsLink && !validLink) return
    lock.current = true; setBusy(true)
    const abort = new AbortController(); controller.current = abort
    const generation = linkGeneration.current
    try {
      const body = kind === 'register' ? { email: email.trim(), password, fullName: fullName.trim(), phone: phone.trim() || null }
        : kind === 'login' ? { email: email.trim(), password }
        : kind === 'verify-email' ? { userId: link.userId, token: link.token } : kind === 'reset-password' ? { userId: link.userId, token: link.token, newPassword: password } : { email: email.trim() }
      await authCommand(kind, body, abort.signal)
      if (abort.signal.aborted || generation !== linkGeneration.current) return
      setPassword(''); setConfirmation('')
      if (kind === 'login') {
        const session = await auth.refresh()
        if (!session) throw new ApiError(401, 'UNAUTHENTICATED')
        navigate(search.get('returnTo') ? safeReturnPath(search.get('returnTo')) : session.roles.includes('Admin') ? '/admin' : '/account', { replace: true })
      } else if (kind === 'verify-email') setSuccess('Email đã được xác minh. Bạn có thể đăng nhập.')
      else if (kind === 'reset-password') { auth.clear(); setSuccess('Mật khẩu đã được cập nhật. Vui lòng đăng nhập lại.') }
      else setSuccess(neutral)
    } catch (failure) {
      if (!abort.signal.aborted && generation === linkGeneration.current) setError(failure instanceof ApiError ? failure : new ApiError(0, 'NETWORK_ERROR'))
    } finally {
      if (!abort.signal.aborted && generation === linkGeneration.current) { setBusy(false); lock.current = false }
    }
  }
  return <section className="auth-page"><div className="auth-intro"><span className="eyebrow">KHÔNG GIAN CỦA BẠN</span><h1>{titles[kind]}</h1><p>{kind === 'register' ? 'Một tài khoản để đồng hành cùng chiếc điện thoại của bạn.' : 'Kết nối lại với những điều dành riêng cho bạn.'}</p><div className="auth-decoration" aria-hidden="true">↗</div></div>
    <div className="auth-card">
      {needsLink && !validLink ? <div className="notice notice-error" role="alert">Liên kết không hợp lệ hoặc đã thiếu thông tin. Vui lòng yêu cầu email mới.</div> : <form className="auth-form" noValidate onSubmit={submit}>
        {hasEmail && <TextField label="Email" type="email" autoComplete="email" required maxLength={256} value={email} onChange={event => setEmail(event.target.value)} error={field('email')} />}
        {kind === 'register' && <><TextField label="Họ tên" autoComplete="name" required maxLength={150} value={fullName} onChange={event => setFullName(event.target.value)} error={field('fullName')} /><TextField label="Số điện thoại (không bắt buộc)" type="tel" autoComplete="tel" maxLength={30} value={phone} onChange={event => setPhone(event.target.value)} error={field('phone')} /></>}
        {hasPassword && <TextField label={kind === 'reset-password' ? 'Mật khẩu mới' : 'Mật khẩu'} type="password" autoComplete={newPassword ? 'new-password' : 'current-password'} required maxLength={128} value={password} onChange={event => setPassword(event.target.value)} hint={newPassword ? 'Ít nhất 12 ký tự, gồm chữ hoa, chữ thường, số và ký tự đặc biệt.' : undefined} error={field(kind === 'reset-password' ? 'newPassword' : 'password') ?? field('password')} />}
        {newPassword && <TextField label="Nhập lại mật khẩu" type="password" autoComplete="new-password" required maxLength={128} value={confirmation} onChange={event => setConfirmation(event.target.value)} error={field('confirmation')} />}
        {kind === 'verify-email' && <p>Xác nhận email để hoàn tất việc tạo tài khoản.</p>}
        {error && <ErrorNotice error={error} />}
        {!success && <SubmitButton busy={busy}>{busy ? 'Đang xử lý…' : titles[kind]}</SubmitButton>}
      </form>}
      {success && <div className="notice notice-success" role="status">{success}</div>}
      <nav className="auth-links" aria-label="Tùy chọn tài khoản">
        {kind !== 'login' && <Link to="/auth/login">Đăng nhập</Link>}
        {kind === 'login' && <><Link to="/auth/register">Tạo tài khoản</Link><Link to="/auth/forgot-password">Quên mật khẩu?</Link></>}
        {['login', 'register', 'verify-email', 'resend-verification'].includes(kind) && <Link to="/auth/resend-verification">Yêu cầu email xác minh mới</Link>}
        {kind === 'reset-password' && <Link to="/auth/forgot-password">Yêu cầu liên kết đặt lại mới</Link>}
      </nav>
    </div></section>
}

export function AccountPage() {
  const auth = useAuth(); const navigate = useNavigate(); const [busy, setBusy] = useState(false); const [error, setError] = useState<ApiError>()
  const lock = useRef(false)
  const controller = useRef<AbortController | null>(null)
  useEffect(() => () => { controller.current?.abort() }, [])
  async function logout() {
    if (lock.current) return
    lock.current = true; setBusy(true); setError(undefined)
    const abort = new AbortController(); controller.current = abort
    try { await authCommand('logout', {}, abort.signal); if (!abort.signal.aborted) { auth.clear(); navigate('/auth/login', { replace: true }) } }
    catch (failure) { if (!abort.signal.aborted) setError(failure instanceof ApiError ? failure : new ApiError(0, 'NETWORK_ERROR')) }
    finally { lock.current = false; if (!abort.signal.aborted) setBusy(false) }
  }
  return <section className="page-section"><span className="eyebrow">TÀI KHOẢN</span><h1>Xin chào, {auth.user?.fullName}</h1><div className="health-card"><p><Link className="button button-outline" to="/account/orders">Đơn hàng của tôi</Link> <Link className="button button-outline" to="/account/notifications">Thông báo</Link></p>{auth.user?.roles.includes('Admin') && <p><Link className="button button-outline" to="/admin">Vào quản trị</Link></p>}{error && <ErrorNotice error={error} />}<SubmitButton busy={busy} onClick={() => { void logout() }}>{busy ? 'Đang đăng xuất…' : 'Đăng xuất'}</SubmitButton></div><ProfilePanel /></section>
}
