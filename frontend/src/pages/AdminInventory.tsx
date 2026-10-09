import { QuantityField } from '../components/QuantityField'
import { useRef, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router'
import { useAuth } from '../auth/AuthContext'
import { ErrorNotice, SubmitButton, TextField } from '../components/primitives'
import { formatVietnamTime } from '../lib/contracts'
import { ApiError, fieldError } from '../services/errors'
import { clearPending, inventoryApi, readPending, storePending } from '../services/inventory'
import type { Balance, StockCommand } from '../services/inventory'
import { ResourceNotice, SelectField, Status } from './AdminCatalogShared'
import { useMutation, useResource } from './adminCatalogHooks'
import type { MutationGate } from './adminCatalogHooks'

export function AdminInventoryPage() {
  const [params, setParams] = useSearchParams()
  const query = new URLSearchParams()
  for (const key of ['search', 'productId', 'page', 'pageSize']) if (params.get(key)) query.set(key, params.get(key)!)
  const resource = useResource(query.toString(), signal => inventoryApi.list(query, signal))
  const page = resource.data
  return <section className="page-section"><span className="eyebrow">QUẢN TRỊ KHO</span><h1>Tồn kho</h1><p className="muted">Tồn khả dụng = trong kho − đã giữ. Chọn đúng SKU để nhập hoặc điều chỉnh.</p>
    <form className="catalog-filters" onSubmit={e => { e.preventDefault(); const text = String(new FormData(e.currentTarget).get('search') ?? '').trim(); const next = new URLSearchParams(params); next.delete('page'); if (text) next.set('search', text); else next.delete('search'); setParams(next) }}><TextField key={params.get('search') ?? ''} label="Tìm SKU hoặc tên sản phẩm" name="search" defaultValue={params.get('search') ?? ''} maxLength={200} /><SubmitButton>Tìm kiếm kho</SubmitButton><SubmitButton type="button" className="button-outline" onClick={() => setParams({})}>Xóa bộ lọc kho</SubmitButton></form>
    {!page ? <ResourceNotice loadingText="Đang tải tồn kho…" error={resource.error} reload={resource.reload} /> : <>{page.items.length === 0 ? <p className="notice">Chưa có phiên bản phù hợp. <Link to="/admin/products">Quản lý sản phẩm</Link></p> : <ul className="catalog-list">{page.items.map(x => <li key={x.variantId} className="catalog-row"><div><h2>{x.sku}</h2><p>{x.productName} · {x.color} · {x.storageGb} GB / RAM {x.ramGb} GB</p><Status active={x.isActive} /><BalanceNumbers balance={x} /></div><Link className="button button-outline" to={`/admin/inventory/${x.variantId}`}>Quản lý kho {x.sku}</Link></li>)}</ul>}
      <nav className="catalog-pagination" aria-label="Phân trang tồn kho"><SubmitButton type="button" disabled={page.page <= 1} onClick={() => { const next = new URLSearchParams(params); next.set('page', String(page.page - 1)); setParams(next) }}>Trang kho trước</SubmitButton><span>Trang {page.page} / {Math.max(1, Math.ceil(page.totalCount / page.pageSize))} · {page.totalCount} phiên bản</span><SubmitButton type="button" disabled={page.page * page.pageSize >= page.totalCount} onClick={() => { const next = new URLSearchParams(params); next.set('page', String(page.page + 1)); setParams(next) }}>Trang kho sau</SubmitButton></nav></>}
  </section>
}
function BalanceNumbers({ balance }: { balance: Balance }) {
  return <dl className="inventory-balances"><div><dt>Trong kho</dt><dd>{balance.onHand.toLocaleString('vi-VN')}</dd></div><div><dt>Đã giữ</dt><dd>{balance.reserved.toLocaleString('vi-VN')}</dd></div><div><dt>Khả dụng</dt><dd>{balance.available.toLocaleString('vi-VN')}</dd></div></dl>
}
export function AdminInventoryDetailPage() {
  const { variantId = '' } = useParams(); const auth = useAuth()
  const resource = useResource(variantId, signal => inventoryApi.detail(variantId, signal))
  return <section className="page-section"><Link to="/admin/inventory">← Danh sách tồn kho</Link><h1>Nhập và điều chỉnh kho</h1>{!resource.data || !auth.user ? <ResourceNotice loadingText="Đang tải tồn kho…" error={resource.error} reload={resource.reload} /> : <StockEditor key={`${auth.user.userId}:${variantId}`} initial={resource.data} actor={auth.user.userId} />}</section>
}
function StockEditor({ initial, actor }: { initial: Balance; actor: string }) {
  const [balance, setBalance] = useState(initial)
  const [restored] = useState(() => { try { return { command: readPending(actor, initial.variantId) } } catch (e) { return { error: e instanceof ApiError ? e : new ApiError(0, 'OPERATION_STORAGE_UNAVAILABLE') } } })
  const [pending, setPending] = useState<StockCommand | undefined>(restored.command)
  const commandRef = useRef<StockCommand | undefined>(restored.command)
  const [kind, setKind] = useState<'receive' | 'adjust'>('receive')
  const [quantity, setQuantity] = useState(''); const [reason, setReason] = useState('')
  const [invalid, setInvalid] = useState(''); const [storageError, setStorageError] = useState(restored.error)
  const [confirmClear, setConfirmClear] = useState(false); const [historyPage, setHistoryPage] = useState(1)
  const [gate] = useState<MutationGate>(() => { let pending = false; return { acquire: () => { if (pending) return false; pending = true; return true }, release: () => { pending = false } } })
  const action = useMutation(gate)
  const history = useResource(`${balance.variantId}:${historyPage}`, signal => inventoryApi.movements(balance.variantId, historyPage, signal))
  const refresh = useMutation(gate)
  function submit() {
    if (action.busy || refresh.busy || storageError) return
    let command = commandRef.current
    if (!command) {
      const delta = Number(quantity)
      if (!/^-?\d+$/.test(quantity) || !Number.isInteger(delta) || delta === 0 || delta < -2147483648 || delta > 2147483647 || (kind === 'receive' && delta < 1) || !reason.trim()) { setInvalid('Nhập số lượng nguyên khác 0; nhập kho phải lớn hơn 0. Lý do không được bỏ trống.'); return }
      command = { variantId: balance.variantId, kind, delta, reason: reason.trim().normalize('NFC'), operationKey: crypto.randomUUID() }
      try { storePending(actor, command) } catch (e) { setStorageError(e instanceof ApiError ? e : new ApiError(0, 'OPERATION_STORAGE_UNAVAILABLE')); return }
      commandRef.current = command; setPending(command)
    }
    setInvalid(''); const submitted = command
    void action.run(signal => inventoryApi.write(submitted, signal), result => {
      setBalance(result.inventory)
      // Only remove the recovery key after receiving a validated success from the server.
      clearPending(actor, balance.variantId); commandRef.current = undefined; setPending(undefined); setQuantity(''); setReason(''); setConfirmClear(false); setHistoryPage(1); history.reload()
    }, 'Thao tác kho đã được xác nhận. Lịch sử chỉ ghi một lần cho cùng khóa thao tác.')
  }
  return <>
    <section className="catalog-panel"><h2>{balance.sku}</h2><p>{balance.productName} · {balance.color} · {balance.storageGb} GB / RAM {balance.ramGb} GB</p><BalanceNumbers balance={balance} /><p><Link to={`/admin/products/${balance.productId}`}>Xem sản phẩm</Link></p><p className="muted">Lượng đã giữ được quản lý bởi luồng đơn hàng, không chỉnh trực tiếp ở đây.</p></section>
    <section className="catalog-panel"><h2>Ghi thao tác kho</h2>{action.error && <ErrorNotice error={action.error} />}{storageError && <ErrorNotice error={storageError} />}{action.success && <p className="notice" role="status">{action.success}</p>}{invalid && <p className="field-error" role="alert">{invalid}</p>}
      {pending ? <div className="notice"><h3>Thao tác đang chờ xác nhận</h3><p>{pending.kind === 'receive' ? 'Nhập kho' : 'Điều chỉnh'}: {pending.delta > 0 ? '+' : ''}{pending.delta} · {pending.reason}</p><small className="inventory-key">Mã thao tác: {pending.operationKey}</small><p>Giữ nguyên nội dung và mã khi thử lại, kể cả sau tải lại trang. Kiểm tra lịch sử trước khi bỏ thao tác đang chờ.</p><div className="catalog-actions"><SubmitButton type="button" busy={action.busy} disabled={refresh.busy || !!storageError} onClick={submit}>Thử lại cùng thao tác</SubmitButton><SubmitButton type="button" disabled={action.busy || refresh.busy} className="button-outline" onClick={() => setConfirmClear(true)}>Bỏ thao tác sau khi kiểm tra</SubmitButton></div>{confirmClear && <div className="inventory-confirm"><p>Nếu lần gửi trước đã ghi kho, gửi nội dung đó với mã mới sẽ ghi thêm lần nữa. Bạn đã kiểm tra lịch sử và muốn bỏ thao tác này?</p><SubmitButton type="button" disabled={action.busy || refresh.busy} onClick={() => { try { clearPending(actor, balance.variantId); commandRef.current = undefined; setPending(undefined); action.clear(); setConfirmClear(false) } catch { setStorageError(new ApiError(0, 'OPERATION_STORAGE_UNAVAILABLE')) } }}>Xác nhận đã kiểm tra lịch sử</SubmitButton><SubmitButton type="button" className="button-outline" onClick={() => setConfirmClear(false)}>Giữ thao tác</SubmitButton></div>}</div> : <form onSubmit={e => { e.preventDefault(); submit() }}><fieldset disabled={action.busy || refresh.busy || !!storageError}><SelectField label="Loại thao tác" value={kind} onChange={v => setKind(v === 'adjust' ? 'adjust' : 'receive')}><option value="receive">Nhập kho</option><option value="adjust">Điều chỉnh tăng / giảm</option></SelectField><QuantityField label="Số lượng thay đổi" required min={kind === 'receive' ? 1 : -2147483648} hint={kind === 'receive' ? 'Số nguyên dương, ví dụ 10.' : 'Số nguyên khác 0; ví dụ -2 để giảm, 3 để tăng.'} value={quantity} onChange={setQuantity} error={fieldError(action.error?.errors ?? {}, kind === 'receive' ? 'quantity' : 'quantityDelta')} /><TextField label="Lý do" required maxLength={300} value={reason} onChange={e => setReason(e.target.value)} error={fieldError(action.error?.errors ?? {}, 'reason')} /><SubmitButton busy={action.busy}>Ghi thao tác kho</SubmitButton></fieldset></form>}
    </section>
    <section className="catalog-panel"><div className="catalog-heading"><h2>Lịch sử kho</h2><SubmitButton type="button" busy={refresh.busy} disabled={action.busy} className="button-outline" onClick={() => { void refresh.run(signal => inventoryApi.detail(balance.variantId, signal), next => { setBalance(next); history.reload() }, 'Đã tải lại tồn kho.') }}>Tải lại kho và lịch sử</SubmitButton></div>{refresh.error && <ErrorNotice error={refresh.error} />}{refresh.success && <p role="status">{refresh.success}</p>}
      {!history.data ? <ResourceNotice loadingText="Đang tải lịch sử kho…" error={history.error} reload={history.reload} /> : <>{history.data.items.length === 0 ? <p>Chưa có lịch sử kho.</p> : <ul className="catalog-list">{history.data.items.map(x => <li key={x.id} className="inventory-movement"><div><strong>{({ Receive: 'Nhập kho', Adjust: 'Điều chỉnh', Reserve: 'Giữ hàng', Release: 'Trả giữ hàng', Dispatch: 'Xuất kho', CancelReturn: 'Nhập lại hàng hủy' })[x.kind]}</strong><time dateTime={x.createdAt}>{formatVietnamTime(x.createdAt)}</time></div><p>Trong kho: {x.onHandDelta > 0 ? '+' : ''}{x.onHandDelta} · Đã giữ: {x.reservedDelta > 0 ? '+' : ''}{x.reservedDelta}</p><p>{x.reason}</p>{x.operationKey && <small className="inventory-key">Mã thao tác: {x.operationKey}</small>}<small>{x.actorUserId ? 'Người thực hiện: ' + x.actorUserId : 'Người thực hiện: hệ thống'}</small></li>)}</ul>}<nav className="catalog-pagination" aria-label="Phân trang lịch sử kho"><SubmitButton type="button" disabled={historyPage <= 1} onClick={() => setHistoryPage(x => x - 1)}>Lịch sử trước</SubmitButton><span>Trang {history.data.page} / {Math.max(1, Math.ceil(history.data.totalCount / history.data.pageSize))} · {history.data.totalCount} thao tác</span><SubmitButton type="button" disabled={historyPage * 10 >= history.data.totalCount} onClick={() => setHistoryPage(x => x + 1)}>Lịch sử sau</SubmitButton></nav></>}
    </section>
  </>
}
