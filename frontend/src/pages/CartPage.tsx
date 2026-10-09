import { emptyDestination } from '../services/locations'
import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { useCart } from '../cart/CartContext'
import { EmptyState, ErrorNotice, SubmitButton } from '../components/primitives'
import { QuantityField } from '../components/QuantityField'
import { AddressFields } from '../components/AddressFields'
import { formatVnd } from '../lib/contracts'
import { ApiError } from '../services/errors'
import { quotes } from '../services/quotes'
import type { Quote } from '../services/quotes'
import { cartDisplay } from '../services/cartDisplay'
import type { CartVariant } from '../services/cartDisplay'
import { CartImage } from '../components/CartImage'

export function CartPage() {
  const cart = useCart()
  const [destination, setDestination] = useState(emptyDestination)
  const [drafts, setDrafts] = useState<Record<string, string>>({})
  const [errorState, setErrorState] = useState<{ key: string; error: ApiError }>()
  const [quoteState, setQuoteState] = useState<{ key: string; quote: Quote; changed: boolean; confirmed: boolean }>()
  const [busyKey, setBusyKey] = useState<string>()
  const pending = useRef<AbortController | null>(null)
  const idsKey = JSON.stringify(cart.items.map(item => item.variantId))
  const [display, setDisplay] = useState<{ key: string; items?: CartVariant[]; error?: ApiError }>()
  const [displayAttempt, setDisplayAttempt] = useState(0)
  const details = display?.key === idsKey ? display : undefined
  useEffect(() => {
    const ids = JSON.parse(idsKey) as string[]
    if (!ids.length) return
    const abort = new AbortController()
    cartDisplay.get(ids, abort.signal).then(items => { if (!abort.signal.aborted) setDisplay({ key: idsKey, items }) })
      .catch(e => { if (!abort.signal.aborted) setDisplay({ key: idsKey, error: e instanceof ApiError ? e : new ApiError(0, 'NETWORK_ERROR') }) })
    return () => abort.abort()
  }, [idsKey, displayAttempt])
  const key = JSON.stringify([cart.items, destination])
  const busy = busyKey === key
  const error = errorState?.key === key ? errorState.error : undefined
  const setError = (value: ApiError | undefined) => setErrorState(value ? { key, error: value } : undefined)
  const dirty = cart.items.some(x => drafts[x.variantId] !== undefined && drafts[x.variantId] !== String(x.quantity))
  const shown = !dirty && !cart.error && quoteState?.key === key ? quoteState : undefined
  useEffect(() => { pending.current?.abort(); pending.current = null }, [key])
  useEffect(() => () => pending.current?.abort(), [])
  async function calculate() {
    if (pending.current) return
    if (!destination.addressLine.trim() || !destination.provinceCode || !destination.wardCode || dirty) { setError(new ApiError(400, 'VALIDATION_ERROR', undefined, { cart: ['Nhập số nhà, đường; chọn tỉnh/thành phố và phường/xã. Nhấn Enter để lưu số lượng hợp lệ.'] })); return }
    const controller = new AbortController(); pending.current = controller; setBusyKey(key); setError(undefined)
    // Retain the old hash only in memory. A changed price is never silently acknowledged.
    try {
      let changed = false, quote: Quote
      try { quote = await quotes.get(cart.items, destination, shown?.quote.quoteHash, controller.signal) }
      catch (e) { if (!(e instanceof ApiError) || e.code !== 'PRICE_CHANGED') throw e; changed = true; quote = await quotes.get(cart.items, destination, undefined, controller.signal) }
      if (!controller.signal.aborted) setQuoteState({ key, quote, changed: changed || (shown?.changed === true && !shown.confirmed), confirmed: false })
    } catch (e) { if (!controller.signal.aborted) { setQuoteState(undefined); setError(e instanceof ApiError ? e : new ApiError(0, 'INVALID_RESPONSE')) } }
    finally { if (pending.current === controller) { pending.current = null; setBusyKey(undefined) } }
  }
  return <section className="page-section"><span className="eyebrow">GIỎ CỦA BẠN</span><h1>Giỏ hàng</h1>
    {cart.error && <><ErrorNotice error={cart.error} /><SubmitButton type="button" onClick={cart.clear}>Xóa giỏ để khôi phục</SubmitButton></>}
    {!cart.items.length ? <EmptyState title="Giỏ hàng đang trống" action={<Link className="button" to="/products">Chọn điện thoại</Link>}><p>Thêm phiên bản bạn muốn mua từ trang sản phẩm.</p></EmptyState> : <>
      {!cart.error && !dirty && <Link className="button" to="/checkout">Tiến hành đặt hàng</Link>}
      {details?.error && <div className="cart-display-error"><ErrorNotice error={details.error} /><SubmitButton type="button" className="button-outline" onClick={() => setDisplayAttempt(n => n + 1)}>Tải lại thông tin sản phẩm</SubmitButton></div>}
      <ul className="cart-items">{cart.items.map(item => {
        const line = shown?.quote.items.find(x => x.variantId === item.variantId)
        const info = details?.items?.find(x => x.variantId === item.variantId)
        const product = info ?? line
        const title = product?.productName ?? (details?.items ? 'Sản phẩm không còn được bán' : details?.error ? 'Chưa tải được thông tin sản phẩm' : 'Đang tải sản phẩm…')
        const variantName = product ? `${product.color} · ${product.storageGb} GB · RAM ${product.ramGb} GB` : ''
        const draft = drafts[item.variantId] ?? String(item.quantity)
        return <li key={item.variantId}>
        <CartImage url={info?.imageUrl ?? null} alt={info?.imageAltText || `${title}${variantName ? ' – ' + variantName : ''}`} />
        <div className="cart-item-content"><div className="cart-item-heading">{product ? <Link to={'/products/' + product.productSlug}><h2>{title}</h2></Link> : <h2>{title}</h2>}
          <button type="button" className="cart-remove" aria-label={`Xóa ${title}${variantName ? ', ' + variantName : ''}`} title="Xóa khỏi giỏ hàng" onClick={() => cart.remove(item.variantId)}><svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M3 6h18M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2M5 6l1 14a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1l1-14M10 10v7M14 10v7" /></svg></button>
        </div>{product && <><p className="cart-variant-name">{variantName}</p><small>SKU: {product.sku}{line ? ` · Còn ${line.available} sản phẩm` : ''}</small></>}
        {line && <p>{formatVnd(line.unitPrice)} / sản phẩm · {formatVnd(line.lineTotal)}</p>}
        <form noValidate onSubmit={e => { e.preventDefault(); if (!/^[1-9]\d*$/.test(draft) || !Number.isSafeInteger(Number(draft)) || Number(draft) > 2147483647) { setError(new ApiError(400, 'VALIDATION_ERROR', undefined, { quantity: ['Số lượng phải là số nguyên từ 1 đến 2147483647.'] })); return } cart.update(item.variantId, Number(draft)); setDrafts(d => { const next = { ...d }; delete next[item.variantId]; return next }) }}><QuantityField label="Số lượng" accessibleLabel={`Số lượng ${title}${variantName ? ", " + variantName : ""}`} hint="Nhấn Enter để lưu số lượng." value={draft} onChange={value => { pending.current?.abort(); pending.current = null; setBusyKey(undefined); setQuoteState(undefined); setDrafts(d => ({ ...d, [item.variantId]: value })) }} /></form></div>
      </li> })}</ul>
      <form className="catalog-panel" noValidate onSubmit={e => { e.preventDefault(); void calculate() }}><h2>Giao hàng</h2><AddressFields value={destination} onChange={value => { pending.current?.abort(); pending.current = null; setBusyKey(undefined); setDestination(value); setQuoteState(undefined) }} /><p className="muted">TP. Hồ Chí Minh miễn phí; các tỉnh/thành phố khác 30.000 VND mỗi đơn.</p><SubmitButton busy={busy} disabled={dirty || !!cart.error}>Tính lại báo giá</SubmitButton></form>
      {error && <ErrorNotice error={error} />}
      {shown && !busy && !error && <section className="catalog-panel cart-quote" aria-label="Báo giá"><h2>Báo giá hiện tại</h2><dl><div><dt>Tiền hàng</dt><dd>{formatVnd(shown.quote.subtotal)}</dd></div><div><dt>Giảm giá</dt><dd>{formatVnd(shown.quote.discountTotal)}</dd></div><div><dt>Phí giao hàng</dt><dd>{formatVnd(shown.quote.shippingFee)}</dd></div><div><dt>Tổng cộng</dt><dd>{formatVnd(shown.quote.grandTotal)}</dd></div></dl>{shown.changed && !shown.confirmed && <p className="notice notice-error" role="alert">Giá hoặc phí đã thay đổi. Xem báo giá mới và xác nhận lại.</p>}{!shown.confirmed ? <SubmitButton type="button" onClick={() => setQuoteState({ ...shown, confirmed: true })}>Xác nhận báo giá hiện tại</SubmitButton> : <p className="notice" role="status">Bạn đã xác nhận báo giá hiện tại.</p>}<p className="muted">Báo giá chưa giữ hàng. Giá và tồn được kiểm tra lại khi đặt đơn.</p></section>}
    </>}
  </section>
}
