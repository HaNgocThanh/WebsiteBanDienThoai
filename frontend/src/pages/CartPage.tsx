import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { useCart } from '../cart/CartContext'
import { EmptyState, ErrorNotice, SubmitButton } from '../components/primitives'
import { QuantityField } from '../components/QuantityField'
import { ApiError } from '../services/errors'
import { cartDisplay } from '../services/cartDisplay'
import type { CartVariant } from '../services/cartDisplay'
import { CartImage } from '../components/CartImage'

export function CartPage() {
  const cart = useCart()
  const [drafts, setDrafts] = useState<Record<string, string>>({})
  const [errorState, setErrorState] = useState<{ key: string; error: ApiError }>()
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
  const key = JSON.stringify(cart.items)
  const error = errorState?.key === key ? errorState.error : undefined
  const setError = (value: ApiError | undefined) => setErrorState(value ? { key, error: value } : undefined)
  const dirty = cart.items.some(x => drafts[x.variantId] !== undefined && drafts[x.variantId] !== String(x.quantity))
  return <section className="page-section"><span className="eyebrow">GIỎ CỦA BẠN</span><h1>Giỏ hàng</h1>
    {cart.error && <><ErrorNotice error={cart.error} /><SubmitButton type="button" onClick={cart.clear}>Xóa giỏ để khôi phục</SubmitButton></>}
    {!cart.items.length ? <EmptyState title="Giỏ hàng đang trống" action={<Link className="button" to="/products">Chọn điện thoại</Link>}><p>Thêm phiên bản bạn muốn mua từ trang sản phẩm.</p></EmptyState> : <>
      {details?.error && <div className="cart-display-error"><ErrorNotice error={details.error} /><SubmitButton type="button" className="button-outline" onClick={() => setDisplayAttempt(n => n + 1)}>Tải lại thông tin sản phẩm</SubmitButton></div>}
      <ul className="cart-items">{cart.items.map(item => {
        const info = details?.items?.find(x => x.variantId === item.variantId)
        const product = info
        const title = product?.productName ?? (details?.items ? 'Sản phẩm không còn được bán' : details?.error ? 'Chưa tải được thông tin sản phẩm' : 'Đang tải sản phẩm…')
        const variantName = product ? `${product.color} · ${product.storageGb} GB · RAM ${product.ramGb} GB` : ''
        const draft = drafts[item.variantId] ?? String(item.quantity)
        return <li key={item.variantId}>
        <CartImage url={info?.imageUrl ?? null} alt={info?.imageAltText || `${title}${variantName ? ' – ' + variantName : ''}`} />
        <div className="cart-item-content"><div className="cart-item-heading">{product ? <Link to={'/products/' + product.productSlug}><h2>{title}</h2></Link> : <h2>{title}</h2>}
          <button type="button" className="cart-remove" aria-label={`Xóa ${title}${variantName ? ', ' + variantName : ''}`} title="Xóa khỏi giỏ hàng" onClick={() => cart.remove(item.variantId)}><svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M3 6h18M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2M5 6l1 14a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1l1-14M10 10v7M14 10v7" /></svg></button>
        </div>{product && <><p className="cart-variant-name">{variantName}</p><small>SKU: {product.sku}</small></>}
        <form noValidate onSubmit={e => { e.preventDefault(); if (!/^[1-9]\d*$/.test(draft) || !Number.isSafeInteger(Number(draft)) || Number(draft) > 2147483647) { setError(new ApiError(400, 'VALIDATION_ERROR', undefined, { quantity: ['Số lượng phải là số nguyên từ 1 đến 2147483647.'] })); return } cart.update(item.variantId, Number(draft)); setDrafts(d => { const next = { ...d }; delete next[item.variantId]; return next }) }}><QuantityField label="Số lượng" accessibleLabel={`Số lượng ${title}${variantName ? ", " + variantName : ""}`} hint="Nhấn Enter để lưu số lượng." value={draft} onChange={value => { setDrafts(d => ({ ...d, [item.variantId]: value })) }} /></form></div>
      </li> })}</ul>
      {error && <ErrorNotice error={error} />}
      <p className="muted">Địa chỉ giao hàng, phí vận chuyển và tổng tiền sẽ được xác nhận khi thanh toán.</p>
      {!cart.error && !dirty && <Link className="button" to="/checkout">Tiến hành thanh toán</Link>}
    </>}
  </section>
}
