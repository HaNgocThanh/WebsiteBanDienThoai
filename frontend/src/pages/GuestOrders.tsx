import { useEffect, useRef, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router'
import { useAuth } from '../auth/AuthContext'
import { captureGuestLink, forgetGuestLink } from '../checkout/guestLink'
import { EmptyState, ErrorNotice, SubmitButton, TextField } from '../components/primitives'
import { formatVietnamTime, formatVnd } from '../lib/contracts'
import { ApiError, fieldError } from '../services/errors'
import { orders, orderStatuses } from '../services/orders'
import type { GuestOrder } from '../services/orders'
import { OrderReceipt } from './CheckoutPage'
import { OrderPayments } from './OrderPayments'

const failure = (e: unknown) => e instanceof ApiError ? e : new ApiError(0, 'NETWORK_ERROR')
const neutral = 'Nếu thông tin phù hợp, bạn sẽ nhận được liên kết qua email. Hãy kiểm tra hộp thư và thư rác. Liên kết dùng một lần, có hiệu lực 1 giờ; gửi lại không kéo dài hạn thanh toán của đơn.'
export function GuestLookupPage() {
  const [number, setNumber] = useState(''), [email, setEmail] = useState(''), [purpose, setPurpose] = useState<'ViewOrder' | 'ClaimOrder'>('ViewOrder')
  const [error, setError] = useState<ApiError>(), [validation, setValidation] = useState<Record<string, string[]>>({}), [sent, setSent] = useState(false), [busy, setBusy] = useState(false)
  const lock = useRef(false)
  async function submit() {
    if (lock.current) return
    setSent(false); setError(undefined); const fields: Record<string, string[]> = {}
    if (!number.trim()) fields.orderNumber = ['Nhập mã đơn từ trang xác nhận hoặc email.']
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) fields.email = ['Nhập email đã dùng khi đặt hàng.']
    setValidation(fields); if (Object.keys(fields).length) return
    lock.current = true; setBusy(true)
    try { await orders.requestAccess(number.trim(), email.trim(), purpose); setSent(true) } catch (e) { setError(failure(e)) } finally { lock.current = false; setBusy(false) }
  }
  return <section className="page-section guest-page"><span className="eyebrow">ĐƠN HÀNG CỦA BẠN</span><h1>Tra cứu đơn khách vãng lai</h1><p>Nhập mã đơn và email đặt hàng để nhận liên kết xác thực. Bạn không cần tạo tài khoản để xem đơn.</p>
    <form className="catalog-panel" noValidate onSubmit={e => { e.preventDefault(); void submit() }}><fieldset disabled={busy}><legend>Yêu cầu liên kết qua email</legend><TextField label="Mã đơn hàng" value={number} required maxLength={30} error={fieldError(validation, 'orderNumber')} onChange={e => { setNumber(e.target.value); setSent(false) }} /><TextField label="Email đặt hàng" type="email" required maxLength={256} autoComplete="email" value={email} error={fieldError(validation, 'email')} onChange={e => { setEmail(e.target.value); setSent(false) }} />
      <label className="field">Mục đích<select value={purpose} onChange={e => { setPurpose(e.target.value as typeof purpose); setSent(false) }}><option value="ViewOrder">Xem đơn hàng</option><option value="ClaimOrder">Nhận đơn vào tài khoản</option></select></label></fieldset><SubmitButton busy={busy}>Gửi liên kết qua email</SubmitButton></form>
    {sent && <p className="notice" role="status">{neutral}</p>}{error && <ErrorNotice error={error} />}<Link className="button button-outline" to="/guest/order">Mở đơn trong phiên hiện tại</Link>
  </section>
}
export function GuestAccessPage() {
  const auth = useAuth(), location = useLocation(), navigate = useNavigate()
  const [captured, setCaptured] = useState(() => ({ source: location.hash, link: captureGuestLink(location.hash) }))
  const [error, setError] = useState<ApiError>(), [busy, setBusy] = useState(false), [claimed, setClaimed] = useState<string>()
  const lock = useRef(false), currentSource = useRef(captured.source)
  if (location.hash && location.hash !== captured.source) { setCaptured({ source: location.hash, link: captureGuestLink(location.hash) }); setError(undefined); setClaimed(undefined) }
  useEffect(() => { currentSource.current = captured.source }, [captured.source])
  useEffect(() => { if (location.hash) navigate({ pathname: location.pathname, search: location.search }, { replace: true }) }, [location.hash, location.pathname, location.search, navigate])
  async function open() {
    if (lock.current || !captured.link) return
    const link = captured.link, source = captured.source; lock.current = true; setBusy(true); setError(undefined)
    try {
      if (link.purpose === 'ClaimOrder') { const order = await orders.claim(link.token); if (currentSource.current === source) { setClaimed(order.orderNumber); forgetGuestLink() } }
      else { await orders.exchange(link.token, link.purpose); if (currentSource.current === source) { forgetGuestLink(); navigate('/guest/order', { replace: true }) } }
    } catch (e) { if (currentSource.current === source) setError(failure(e)) } finally { lock.current = false; setBusy(false) }
  }
  const claim = captured.link?.purpose === 'ClaimOrder'
  return <section className="page-section guest-page"><span className="eyebrow">TRUY CẬP AN TOÀN</span><h1>{claim ? 'Nhận đơn vào tài khoản' : 'Mở đơn hàng từ email'}</h1>
    {claimed ? <><p className="notice" role="status">Đơn {claimed} đã được nhận vào tài khoản của bạn. Thông tin và tổng tiền lúc mua được giữ nguyên.</p><Link className="button" to="/account">Về tài khoản</Link></> : !captured.link ? <EmptyState title="Cần liên kết từ email" action={<Link className="button" to="/guest/lookup">Yêu cầu liên kết mới</Link>}><p>Liên kết đã thiếu thông tin. Mở lại email hoặc yêu cầu gửi liên kết mới.</p></EmptyState> : <div className="catalog-panel">
      {claim ? <><p>Đăng nhập bằng tài khoản có email đã xác minh khớp email đặt hàng, rồi xác nhận nhận quyền cho đúng đơn này.</p>{auth.status === 'loading' ? <p role="status">Đang kiểm tra tài khoản…</p> : auth.user ? <><p>Tài khoản đang dùng: <strong>{auth.user.email}</strong></p><SubmitButton type="button" busy={busy} onClick={() => void open()}>Nhận đơn vào tài khoản</SubmitButton></> : <><Link className="button" to="/auth/login?returnTo=%2Fguest%2Faccess">Đăng nhập để nhận đơn</Link><Link className="button button-outline" to="/auth/register?returnTo=%2Fguest%2Faccess">Tạo tài khoản</Link><p className="muted">Sau khi xác minh email, đăng nhập và mở lại liên kết nhận đơn trong email. Tải lại trang sẽ xóa thông tin liên kết đang giữ.</p></>}</> : <><p>Liên kết chỉ mở đúng một đơn hàng. Sau khi xác thực, bạn có thể xem đơn trong 30 phút.</p><SubmitButton type="button" busy={busy} onClick={() => void open()}>Xác thực và xem đơn</SubmitButton></>}
      {error && <ErrorNotice error={error} />}<p className="muted">Liên kết có hiệu lực 1 giờ, dùng một lần. Nếu hết hạn hoặc đã dùng, hãy yêu cầu liên kết mới.</p><Link to="/guest/lookup">Gửi lại liên kết truy cập</Link>
    </div>}
  </section>
}
export function GuestOrderPage() {
  const [order, setOrder] = useState<GuestOrder>(), [error, setError] = useState<ApiError>(), [busy, setBusy] = useState(true), [attempt, setAttempt] = useState(0), [sent, setSent] = useState(false), [sending, setSending] = useState(false)
  const lock = useRef(false)
  useEffect(() => {
    const controller = new AbortController()
    queueMicrotask(() => {
      if (controller.signal.aborted) return
      setBusy(true); setError(undefined); setOrder(undefined)
      orders.guest(controller.signal).then(value => { if (!controller.signal.aborted) setOrder(value) }).catch(e => { if (!controller.signal.aborted) setError(failure(e)) }).finally(() => { if (!controller.signal.aborted) setBusy(false) })
    })
    return () => controller.abort()
  }, [attempt])
  async function setup() { if (lock.current) return; lock.current = true; setSending(true); setSent(false); try { await orders.setup(); setSent(true) } catch (e) { setError(failure(e)) } finally { lock.current = false; setSending(false) } }
  return <section className="page-section"><span className="eyebrow">THÔNG TIN LÚC ĐẶT HÀNG</span><h1>Đơn hàng khách vãng lai</h1>{busy && <p role="status">Đang tải đơn hàng…</p>}{error && <><ErrorNotice error={error} /><SubmitButton type="button" onClick={() => setAttempt(n => n + 1)}>Tải lại đơn</SubmitButton><Link className="button button-outline" to="/guest/lookup">Yêu cầu liên kết mới</Link></>}
    {order && <><OrderReceipt order={order} /><div className="checkout-grid"><section className="catalog-panel"><h2>Sản phẩm đã đặt</h2><ul className="order-lines">{order.items.map(i => <li key={i.id}><strong>{i.productName}</strong><span>{i.variant}</span><small>SKU: {i.sku}</small><span>{i.quantity} × {formatVnd(i.unitPrice - i.unitDiscount)}</span><strong>{formatVnd(i.lineTotal)}</strong></li>)}</ul><dl className="order-totals"><div><dt>Tiền hàng</dt><dd>{formatVnd(order.subtotal)}</dd></div><div><dt>Giảm giá</dt><dd>{formatVnd(order.discountTotal)}</dd></div><div><dt>Phí giao hàng</dt><dd>{formatVnd(order.shippingFee)}</dd></div></dl></section><section className="catalog-panel"><h2>Giao hàng</h2><p>{order.recipientName} · {order.phone}</p><p>{[order.addressLine, order.locality, order.province, 'Việt Nam'].filter(Boolean).join(', ')}</p>{order.note && <p>Ghi chú: {order.note}</p>}<p>Đặt lúc {formatVietnamTime(order.createdAt)}</p><h2>Lịch sử đơn</h2><ol className="order-timeline">{order.timeline.map((h, i) => <li key={i}>{orderStatuses[h.toStatus as keyof typeof orderStatuses]} · {formatVietnamTime(h.createdAt)}</li>)}</ol></section></div>
      <OrderPayments id={order.id} area="guest" />
      {order.createAccountConsent && <section className="catalog-panel"><h2>Quản lý đơn bằng tài khoản</h2><p>Bạn đã đồng ý nhận hướng dẫn tạo tài khoản khi đặt hàng. Việc đăng ký và nhận quyền đơn vẫn cần xác minh email.</p><SubmitButton type="button" busy={sending} onClick={() => void setup()}>Gửi hướng dẫn tạo tài khoản</SubmitButton>{sent && <p className="notice" role="status">{neutral}</p>}</section>}
      <Link className="button button-outline" to="/guest/lookup">Yêu cầu liên kết nhận đơn vào tài khoản</Link>
    </>}
  </section>
}
