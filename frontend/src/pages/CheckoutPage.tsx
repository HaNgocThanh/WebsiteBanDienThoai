import { useEffect, useRef, useState } from 'react'
import { OrderPayments } from './OrderPayments'
import { Link, useNavigate } from 'react-router'
import { useAuth } from '../auth/AuthContext'
import { readCart, useCart } from '../cart/CartContext'
import { cartMatches, readPendingCheckout, savePendingCheckout } from '../checkout/pending'
import type { PendingCheckout } from '../checkout/pending'
import { AddressFields } from '../components/AddressFields'
import { EmptyState, ErrorNotice, SubmitButton, TextField } from '../components/primitives'
import { formatVietnamTime, formatVnd } from '../lib/contracts'
import { ApiError, fieldError } from '../services/errors'
import { emptyDestination } from '../services/locations'
import { orders, orderStatuses } from '../services/orders'
import type { OrderBody, PaymentMethod, PlacedOrder } from '../services/orders'
import { quotes } from '../services/quotes'
import type { Quote } from '../services/quotes'

const failure = (e: unknown) => e instanceof ApiError ? e : new ApiError(0, 'NETWORK_ERROR')
export function OrderReceipt({ order, guest = true }: { order: PlacedOrder; guest?: boolean }) {
  return <section className="catalog-panel order-receipt"><h2>Mã đơn: {order.orderNumber}</h2><dl><div><dt>Trạng thái đơn</dt><dd>{orderStatuses[order.status]}</dd></div><div><dt>Tổng tiền</dt><dd>{formatVnd(order.grandTotal)}</dd></div><div><dt>Phương thức</dt><dd>{order.paymentMethod === 'COD' ? 'Thanh toán khi nhận hàng (COD)' : 'Chuyển khoản ngân hàng'}</dd></div>{order.paymentDueAt && <div><dt>Hạn thanh toán</dt><dd>{formatVietnamTime(order.paymentDueAt)}</dd></div>}</dl>
    <p>{order.paymentMethod === 'COD' ? 'Thanh toán khi nhận hàng. Cửa hàng sẽ liên hệ để xác nhận đơn.' : 'Đơn đã được ghi nhận, chưa phải xác nhận đã thanh toán. Bạn có thể tiếp tục thanh toán qua SePay Sandbox bên dưới.'}</p>
    <p className="muted">{guest ? 'Bạn có thể yêu cầu liên kết xem đơn bằng mã đơn và email đặt hàng.' : 'Đơn hàng được gắn với tài khoản của bạn. Hãy lưu mã đơn để liên hệ cửa hàng khi cần.'}</p><Link className="button button-outline" to={guest ? '/guest/lookup' : '/account'}>{guest ? 'Tra cứu đơn khách vãng lai' : 'Về tài khoản'}</Link></section>
}
export function CheckoutPage() {
  const auth = useAuth(), cart = useCart(), navigate = useNavigate()
  const [destination, setDestination] = useState(emptyDestination)
  const [name, setName] = useState(''), [phone, setPhone] = useState(''), [email, setEmail] = useState(''), [note, setNote] = useState('')
  const [payment, setPayment] = useState<PaymentMethod>('COD'), [consent, setConsent] = useState(false)
  const [validation, setValidation] = useState<Record<string, string[]>>({}), [error, setError] = useState<ApiError>()
  const [quoteState, setQuoteState] = useState<{ key: string; quote: Quote; confirmed: boolean; changed: boolean }>()
  const [busy, setBusy] = useState(false), [uncertain, setUncertain] = useState(false), [recovering, setRecovering] = useState(true)
  const [priceChanged, setPriceChanged] = useState(false)
  const [saved, setSaved] = useState(() => { try { return { pending: readPendingCheckout(), error: undefined as ApiError | undefined } } catch (e) { return { pending: null, error: failure(e) } } })
  const pending = useRef<PendingCheckout | null>(saved.pending), attempt = useRef<OrderBody | null>(null), lock = useRef(false)
  const key = JSON.stringify([cart.items, destination]), owner = auth.user?.userId ?? 'guest', currentOwner = useRef(owner); currentOwner.current = owner
  const currentKey = useRef(key); currentKey.current = key
  const shown = quoteState?.key === key ? quoteState : undefined
  const frozen = busy || uncertain
  function complete(order: PlacedOrder, operation: PendingCheckout) {
    // The committed cart may have changed in another tab while this request awaited the network.
    try { if (cartMatches(operation.items, readCart())) cart.clear() } catch { /* Preserve a corrupt/new cart; the committed order remains successful. */ }
    navigate('/checkout/success', { replace: true, state: { order } })
  }
  useEffect(() => {
    if (auth.status === 'loading') return
    if (!pending.current) { setRecovering(false); return }
    const controller = new AbortController(), operation = pending.current
    setRecovering(true)
    orders.result(operation.key, controller.signal).then(result => {
      if (controller.signal.aborted) return
      if (result.order) complete(result.order, operation)
      else if (result.state === 'Expired') { savePendingCheckout(null); pending.current = null; setSaved({ pending: null, error: undefined }); setError(new ApiError(409, 'CHECKOUT_EXPIRED')) }
    }).catch(e => { if (!controller.signal.aborted) setError(failure(e)) }).finally(() => { if (!controller.signal.aborted) setRecovering(false) })
    return () => controller.abort()
    // Recovery is tied to identity, never to edits of the public cart.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [auth.status, owner])
  function field(n: string) { return fieldError(validation, n) ?? fieldError(error?.errors ?? {}, n) }
  function validate() {
    const errors: Record<string, string[]> = {}
    if (!name.trim()) errors.recipientName = ['Nhập họ tên người nhận.']
    if (!phone.trim()) errors.phone = ['Nhập số điện thoại liên hệ.']
    if (!auth.user && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) errors.email = ['Nhập email hợp lệ để nhận liên kết xem đơn.']
    if (!destination.addressLine.trim() || !destination.provinceCode || !destination.wardCode) errors.address = ['Nhập số nhà, đường và chọn đủ tỉnh/thành phố, phường/xã.']
    setValidation(errors)
    if (Object.keys(errors).length) { requestAnimationFrame(() => document.querySelector<HTMLInputElement>('.checkout-form input[aria-invalid="true"]')?.focus()); return false }
    return true
  }
  async function calculate() {
    if (lock.current || !validate() || !cart.items.length || cart.error) return
    lock.current = true; setBusy(true); setError(undefined)
    const requestKey = key
    try {
      let changed = priceChanged, quote: Quote
      try { quote = await quotes.get(cart.items, destination, shown?.quote.quoteHash) }
      catch (e) { if (!(e instanceof ApiError) || e.code !== 'PRICE_CHANGED') throw e; changed = true; quote = await quotes.get(cart.items, destination) }
      if (currentKey.current === requestKey) setQuoteState({ key: requestKey, quote, confirmed: false, changed })
    } catch (e) { setQuoteState(undefined); setError(failure(e)) }
    finally { lock.current = false; setBusy(false) }
  }
  async function submit() {
    if (lock.current || saved.error || auth.status === 'error' || auth.status === 'loading') return
    if (!attempt.current && (!validate() || !shown?.confirmed || !cart.items.length || cart.error)) return
    lock.current = true; setBusy(true); setError(undefined)
    const requestedOwner = owner
    try {
      if (!pending.current) {
        const session = await orders.session(), operation = { key: session.checkoutKey, items: cart.items.map(i => ({ ...i })) }
        savePendingCheckout(operation); pending.current = operation; setSaved({ pending: operation, error: undefined })
      }
      const operation = pending.current
      const result = await orders.result(operation.key)
      if (result.order) { if (currentOwner.current === requestedOwner) complete(result.order, operation); return }
      if (result.state === 'Expired') { savePendingCheckout(null); pending.current = null; attempt.current = null; setUncertain(false); throw new ApiError(409, 'CHECKOUT_EXPIRED') }
      if (!attempt.current) {
        attempt.current = { ...destination, items: cart.items.map(i => ({ ...i })), recipientName: name.trim(), phone: phone.trim(), note: note.trim(), countryCode: 'VN', paymentMethod: payment, quoteHash: shown!.quote.quoteHash, createAccountConsent: auth.user ? false : consent, ...(auth.user ? {} : { email: email.trim(), consentTextVersion: 'account-create-v1' }) }
        const updated = { key: operation.key, items: attempt.current.items }; savePendingCheckout(updated); pending.current = updated; setSaved({ pending: updated, error: undefined })
      }
      const order = await orders.place(operation.key, attempt.current)
      setUncertain(false)
      if (currentOwner.current === requestedOwner) complete(order, { ...operation, items: attempt.current.items })
    } catch (e) {
      const err = failure(e); setError(err)
      if (err.status === 0 || err.status >= 500 || err.code === 'CHECKOUT_BUSY' || err.code === 'IDEMPOTENCY_PAYLOAD_MISMATCH') setUncertain(true)
      else { attempt.current = null; setUncertain(false); if (err.code === 'PRICE_CHANGED') setPriceChanged(true); if (['PRICE_CHANGED', 'OUT_OF_STOCK', 'VARIANT_UNAVAILABLE', 'CHECKOUT_EXPIRED'].includes(err.code)) setQuoteState(undefined) }
    } finally { lock.current = false; setBusy(false) }
  }
  async function recover() {
    if (lock.current || !pending.current) return
    lock.current = true; setBusy(true); setError(undefined)
    try {
      const result = await orders.result(pending.current.key)
      if (result.order) complete(result.order, pending.current)
      else { setError(new ApiError(409, result.state === 'Expired' ? 'CHECKOUT_EXPIRED' : 'CHECKOUT_NOT_COMPLETED')); if (result.state === 'Expired') { savePendingCheckout(null); pending.current = null; attempt.current = null; setUncertain(false) } }
    } catch (e) { setError(failure(e)) }
    finally { lock.current = false; setBusy(false) }
  }
  if (auth.status === 'loading' || recovering) return <section className="page-section"><h1>Đặt hàng</h1><p role="status">Đang kiểm tra phiên và kết quả đặt hàng…</p></section>
  return <section className="page-section"><span className="eyebrow">HOÀN TẤT LỰA CHỌN</span><h1>Đặt hàng</h1>
    {(error || saved.error || auth.error || cart.error) && <ErrorNotice error={(saved.error ?? auth.error ?? cart.error ?? error)!} />}
    {auth.status === 'error' && <SubmitButton type="button" onClick={() => void auth.refresh().catch(() => {})}>Kiểm tra lại tài khoản</SubmitButton>}
    {error?.code === 'CHECKOUT_NOT_FOUND' && <div className="notice"><p>Hãy tra cứu đơn trước bằng email. Nếu muốn đặt một đơn mới, bạn có thể bắt đầu phiên mới.</p><Link to="/guest/lookup">Tra cứu đơn trước</Link><SubmitButton type="button" className="button-outline" onClick={() => { try { savePendingCheckout(null); pending.current = null; attempt.current = null; setSaved({ pending: null, error: undefined }); setUncertain(false); setError(undefined); setQuoteState(undefined) } catch (e) { setError(failure(e)) } }}>Bắt đầu phiên đặt hàng mới</SubmitButton></div>}
    {!cart.items.length && !pending.current ? <EmptyState title="Giỏ hàng đang trống" action={<Link className="button" to="/products">Chọn điện thoại</Link>}><p>Thêm sản phẩm để đặt hàng.</p></EmptyState> : <>
      {pending.current && <div className="notice"><p>Đang giữ phiên đặt hàng trước. Nếu mất phản hồi, kiểm tra kết quả trước khi đặt đơn mới.</p><SubmitButton type="button" busy={busy} className="button-outline" onClick={() => void recover()}>Kiểm tra kết quả đặt hàng</SubmitButton></div>}
      {uncertain && <p className="notice" role="status">Chưa xác định được kết quả. Thử lại sẽ dùng đúng yêu cầu trước, không tạo đơn trùng. Thông tin đang được giữ để tránh thay đổi khi gửi lại.</p>}
      <div className="checkout-grid"><form id="checkout-order" className="catalog-panel checkout-form" noValidate onSubmit={e => { e.preventDefault(); void submit() }}><fieldset disabled={frozen || !!saved.error || auth.status === 'error'}><legend>Thông tin nhận hàng</legend>
        <TextField label="Họ tên người nhận" required maxLength={150} autoComplete="name" value={name} error={field('recipientName')} onChange={e => setName(e.target.value)} />
        <TextField label="Số điện thoại" type="tel" required maxLength={30} autoComplete="tel" value={phone} error={field('phone')} onChange={e => setPhone(e.target.value)} />
        <TextField label="Email nhận đơn" type="email" required maxLength={256} autoComplete="email" readOnly={!!auth.user} value={auth.user?.email ?? email} error={field('email')} hint={auth.user ? 'Dùng email đã xác minh của tài khoản.' : 'Liên kết xem đơn sẽ gửi đến email này; không cần tài khoản.'} onChange={e => setEmail(e.target.value)} />
        <AddressFields value={destination} onChange={value => { setDestination(value); setQuoteState(undefined) }} />{validation.address && <p className="field-error" role="alert">{validation.address[0]}</p>}
        <TextField label="Ghi chú giao hàng (tùy chọn)" maxLength={1000} value={note} onChange={e => setNote(e.target.value)} />
        <fieldset className="payment-options"><legend>Phương thức thanh toán</legend><label><input type="radio" name="payment" checked={payment === 'COD'} onChange={() => setPayment('COD')} /> Thanh toán khi nhận hàng (COD)</label><label><input type="radio" name="payment" checked={payment === 'BankTransfer'} onChange={() => setPayment('BankTransfer')} /> Chuyển khoản ngân hàng</label></fieldset>
        {payment === 'BankTransfer' && <p className="muted">Đơn chuyển khoản có hạn thanh toán 24 giờ từ lúc tạo đơn. Cửa hàng sẽ liên hệ để hướng dẫn chuyển khoản.</p>}
        {!auth.user && <label className="checkout-consent"><input type="checkbox" checked={consent} onChange={e => setConsent(e.target.checked)} /> Tôi đồng ý nhận hướng dẫn tạo tài khoản để quản lý đơn hàng. Không chọn vẫn đặt hàng được.</label>}
        <SubmitButton type="button" busy={busy} className="button-outline" onClick={() => void calculate()}>Tính báo giá</SubmitButton>
      </fieldset></form>
      <aside className="catalog-panel cart-quote checkout-summary"><h2>Đơn hàng của bạn</h2>{shown ? <><ul className="order-lines">{shown.quote.items.map(i => <li key={i.variantId}><strong>{i.productName}</strong><span>{i.color} · {i.storageGb} GB · RAM {i.ramGb} GB</span><span>{i.quantity} × {formatVnd(i.unitPrice - i.unitDiscount)}</span><strong>{formatVnd(i.lineTotal)}</strong></li>)}</ul><dl><div><dt>Tiền hàng</dt><dd>{formatVnd(shown.quote.subtotal)}</dd></div><div><dt>Giảm giá</dt><dd>{formatVnd(shown.quote.discountTotal)}</dd></div><div><dt>Phí giao hàng</dt><dd>{formatVnd(shown.quote.shippingFee)}</dd></div><div><dt>Tổng cộng</dt><dd>{formatVnd(shown.quote.grandTotal)}</dd></div></dl>{shown.changed && <p className="notice notice-error" role="alert">Giá hoặc phí đã thay đổi. Xem và xác nhận báo giá mới.</p>}<label className="checkout-consent"><input type="checkbox" checked={shown.confirmed} disabled={frozen} onChange={e => setQuoteState({ ...shown, confirmed: e.target.checked })} /> Tôi xác nhận sản phẩm, số lượng và tổng tiền trên.</label></> : <p className="muted">Nhập thông tin nhận hàng và tính báo giá để xem tổng tiền từ cửa hàng.</p>}<p className="muted">TP. Hồ Chí Minh miễn phí; tỉnh/thành khác 30.000 VND/đơn. Báo giá chưa giữ hàng.</p><SubmitButton form="checkout-order" busy={busy} disabled={!!saved.error || auth.status === 'error' || !uncertain && (!shown?.confirmed || !!cart.error)}>{uncertain ? 'Thử lại yêu cầu đặt hàng' : 'Đặt hàng'}</SubmitButton><Link to="/cart">Quay lại giỏ hàng</Link></aside></div>
    </>}
  </section>
}
export function CheckoutSuccessPage() {
  const navigate = useNavigate(), auth = useAuth()
  const [receiptKey, setReceiptKey] = useState<string>(), [order, setOrder] = useState<PlacedOrder>(), [error, setError] = useState<ApiError>(), [busy, setBusy] = useState(true), [attempt, setAttempt] = useState(0)
  useEffect(() => {
    const controller = new AbortController()
    queueMicrotask(() => {
      if (controller.signal.aborted) return
      setBusy(true); setError(undefined)
      try {
      const operation = readPendingCheckout(); if (!operation) { setBusy(false); return }
      orders.result(operation.key, controller.signal).then(result => { if (!controller.signal.aborted) { if (result.order) { setOrder(result.order); setReceiptKey(operation.key) } else setError(new ApiError(409, 'CHECKOUT_NOT_COMPLETED')) } }).catch(e => { if (!controller.signal.aborted) setError(failure(e)) }).finally(() => { if (!controller.signal.aborted) setBusy(false) })
      } catch (e) { setError(failure(e)); setBusy(false) }
    })
    return () => controller.abort()
  }, [attempt])
  return <section className="page-section"><h1>{order ? 'Đặt hàng thành công' : 'Kết quả đặt hàng'}</h1>{busy && <p role="status">Đang kiểm tra kết quả từ cửa hàng…</p>}{error && <><ErrorNotice error={error} /><SubmitButton type="button" onClick={() => setAttempt(n => n + 1)}>Kiểm tra lại</SubmitButton></>}{order && !busy && <><p className="notice" role="status">Đơn hàng đã được ghi nhận. Hãy lưu mã đơn để tra cứu.</p><OrderReceipt order={order} guest={!auth.user} />{order.paymentMethod === 'BankTransfer' && receiptKey && <OrderPayments area="checkout" id={receiptKey} />}<SubmitButton type="button" onClick={() => { try { savePendingCheckout(null); navigate('/products') } catch (e) { setError(failure(e)) } }}>Tiếp tục mua sắm</SubmitButton></>}{!busy && !order && !error && <EmptyState title="Chưa có kết quả đặt hàng" action={<Link className="button" to="/cart">Về giỏ hàng</Link>}><p>Chỉ hiển thị thành công khi cửa hàng đã ghi nhận đơn.</p></EmptyState>}<Link to="/guest/lookup">Yêu cầu liên kết xem đơn</Link></section>
}
