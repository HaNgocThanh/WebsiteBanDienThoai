import { useRef, useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { SubmitButton, TextField } from '../components/primitives'
import { formatVnd } from '../lib/contracts'
import { afterSale, afterSalePath } from '../services/afterSale'
import type { AfterSaleArea, AfterSaleDraft } from '../services/afterSale'
import type { OrderDetail } from '../services/orderManagement'
import { ApiError } from '../services/errors'
import { ActionNotice, ResourceNotice, TextAreaField } from './AdminCatalogShared'
import { useMutation, useResource } from './adminCatalogHooks'
const labels = { Pending: 'Đang chờ', Approved: 'Đã chấp thuận', Rejected: 'Đã từ chối', Completed: 'Đã hoàn tiền', Failed: 'Hoàn tiền thất bại' }
export function OrderAfterSale({ id, area, detail, changed = () => {} }: { id: string; area: AfterSaleArea; detail?: OrderDetail; changed?: () => void }) {
  const auth = useAuth(), resource = useResource((auth.user?.userId ?? 'guest') + ':' + area + ':' + id, signal => afterSale.read(area, id, signal)), data = resource.data
  function reload() { resource.reload(); changed(); window.dispatchEvent(new Event('notifications-changed')) }
  const base = afterSalePath(area, id)
  return <section className="catalog-panel"><h2>Hủy đơn và hoàn tiền</h2><p>Hủy đơn chưa có nghĩa đã hoàn tiền. Hàng đang giao chỉ được nhập lại sau khi cửa hàng xác nhận đã thu hồi.</p>
    {!data ? <ResourceNotice error={resource.error} reload={resource.reload} loadingText="Đang tải thông tin hủy/hoàn…" /> : <>
      {data.canCancel && <AfterSaleForm key={'cancel:' + data.version} path={base + '/cancel'} version={data.version} title="Hủy đơn hàng" reasons={['Không còn nhu cầu mua hàng', 'Đặt nhầm sản phẩm hoặc thông tin', 'Khách hàng yêu cầu hủy']} reload={reload} />}
      {data.canRequest && <AfterSaleForm key={'request:' + data.version} path={base + '/cancellation-requests'} version={data.version} title="Yêu cầu dừng giao và thu hồi" reasons={['Khách hàng yêu cầu dừng giao', 'Thông tin giao hàng cần thay đổi']} reload={reload} />}
      {area === 'guest' && !data.canCancel && !data.canRequest && ['Placed', 'Reviewing', 'Approved', 'Shipping'].includes(data.status) && <p>Cần yêu cầu liên kết “Hủy đơn / yêu cầu thu hồi” qua email để thao tác.</p>}
      {data.requests.map(r => <article className="catalog-panel" key={r.id}><h3>Yêu cầu thu hồi · {labels[r.status]}</h3><p>{r.reason}</p>{r.reviewNote && <p>{r.reviewNote}</p>}{area === 'admin' && r.status === 'Pending' && <>
        {data.status === 'Shipping' && <AfterSaleForm path={base + '/cancellation-requests/' + r.id + '/review'} version={r.version} title="Chấp thuận thu hồi và hủy đơn" reasons={['Đã nhận đủ hàng thu hồi và kiểm tra thực tế']} decision="Approve" recovery reload={reload} />}
        <AfterSaleForm path={base + '/cancellation-requests/' + r.id + '/review'} version={r.version} title="Từ chối yêu cầu thu hồi" reasons={['Chưa đủ điều kiện thu hồi hàng', 'Khách hàng tiếp tục nhận hàng']} decision="Reject" reload={reload} />
      </>}</article>)}
      {data.refunds.map(r => <article className="catalog-panel" key={r.id}><h3>Khoản hoàn {r.id} · {labels[r.status]}</h3><p>Tiền hàng: {formatVnd(r.merchandiseAmount)} · Giao hàng: {formatVnd(r.shippingAmount)}</p>{r.reason && <p>{r.reason}</p>}{r.reference && <p>Mã chứng từ: {r.reference}</p>}{area === 'admin' && r.status === 'Pending' && <>
        <AfterSaleForm path={'/api/v1/admin/refunds/' + r.id + '/complete'} version={r.version} title={'Ghi nhận đã hoàn tiền #' + r.id} reasons={['Đã kiểm tra chứng từ và xác nhận hoàn tiền thực tế']} reference reload={reload} />
        <AfterSaleForm path={'/api/v1/admin/refunds/' + r.id + '/fail'} version={r.version} title={'Ghi nhận hoàn tiền thất bại #' + r.id} reasons={['Giao dịch hoàn tiền thất bại', 'Thông tin nhận tiền chưa hợp lệ']} reload={reload} />
      </>}</article>)}
      {area === 'admin' && ['Cancelled', 'Completed'].includes(data.status) && detail?.payments?.filter(p => p.status === 'Confirmed').map(p => <AfterSaleForm key={p.id + ':' + data.version} path={'/api/v1/admin/payments/' + p.id + '/refunds'} version={data.version} title={'Tạo yêu cầu hoàn tiền cho khoản #' + p.id} reasons={['Hoàn tiền do hủy đơn', 'Hoàn tiền hàng theo kết quả xử lý', 'Hoàn phí giao hàng']} amounts reload={reload} />)}
      <SubmitButton type="button" onClick={reload}>Tải lại thông tin hủy/hoàn</SubmitButton>
    </>}
  </section>
}
export function AfterSaleForm({ path, version, title, reasons, decision, recovery = false, reference = false, amounts = false, reload }: { path: string; version: string; title: string; reasons: string[]; decision?: 'Approve' | 'Reject'; recovery?: boolean; reference?: boolean; amounts?: boolean; reload: () => void }) {
  const [reason, setReason] = useState(''), [note, setNote] = useState(''), [proof, setProof] = useState(''), [merchandise, setMerchandise] = useState('0'), [shipping, setShipping] = useState('0'), [checked, setChecked] = useState(false), [attempt, setAttempt] = useState<{ body: AfterSaleDraft; version: string }>(), [refresh, setRefresh] = useState(false), action = useMutation(), lock = useRef(false)
  function submit() {
    if (lock.current || action.busy || refresh || !checked || !reasons.includes(reason)) return
    const current = attempt ?? { version, body: { reason: reason + (note.trim() ? ' — ' + note.trim() : ''), operationKey: crypto.randomUUID(), ...(decision ? { decision } : {}), ...(recovery ? { goodsRecovered: true } : {}), ...(reference || recovery ? { reference: proof.trim() } : {}), ...(amounts ? { merchandiseAmount: Number(merchandise), shippingAmount: Number(shipping) } : {}) } }
    lock.current = true; setAttempt(current)
    void action.run(async signal => { try { return await afterSale.mutate(path, current.version, current.body, signal) } catch (e) {
      if (e instanceof ApiError && e.status > 0 && e.status < 500 && !['CHECKOUT_BUSY', 'INVALID_RESPONSE'].includes(e.code)) { setAttempt(undefined); setChecked(false); setRefresh(e.status === 409 || e.status === 412) } throw e
    } }, reload, 'Đã ghi nhận thao tác.').finally(() => { lock.current = false })
  }
  return <form className="catalog-panel" onSubmit={e => { e.preventDefault(); submit() }}><h3>{title}</h3>{reference && <p>Chỉ xác nhận sau khi đã hoàn tiền thực tế. Thao tác này ghi nhận chứng từ, không tự gửi tiền qua SePay.</p>}
    <fieldset className="checkout-fields" disabled={!!attempt || refresh || action.busy}><label className="field">Lý do {title}<select required value={reason} onChange={e => setReason(e.target.value)}><option value="">Chọn lý do</option>{reasons.map(r => <option key={r}>{r}</option>)}</select></label><TextAreaField label={'Ghi chú bổ sung ' + title + ' (không bắt buộc)'} value={note} onChange={setNote} maxLength={Math.max(0, 300 - reason.length - 3)} />
      {(reference || recovery) && <TextField label={recovery ? 'Chứng từ thu hồi hàng' : 'Mã giao dịch hoàn tiền'} value={proof} onChange={e => setProof(e.target.value)} maxLength={150} required />}
      {amounts && <><TextField label="Tiền hàng hoàn (VND)" type="number" min={0} step={1} max={Number.MAX_SAFE_INTEGER} required value={merchandise} onChange={e => setMerchandise(e.target.value)} /><TextField label="Phí giao hàng hoàn (VND)" type="number" min={0} step={1} max={Number.MAX_SAFE_INTEGER} required value={shipping} onChange={e => setShipping(e.target.value)} /></>}
      <label className="checkout-consent"><input type="checkbox" required checked={checked} onChange={e => setChecked(e.target.checked)} />{recovery ? 'Tôi đã nhận lại đầy đủ hàng thực tế và kiểm tra chứng từ thu hồi.' : 'Tôi đã kiểm tra và xác nhận: ' + title + '.'}</label></fieldset>
    {attempt && <p>Giữ nguyên yêu cầu để thử lại an toàn khi chưa rõ kết quả.</p>}<SubmitButton busy={action.busy} disabled={refresh || !checked || !reason}>{attempt ? 'Thử lại: ' + title : title}</SubmitButton><ActionNotice action={action} />{refresh && <SubmitButton type="button" onClick={reload}>Tải lại trước khi thao tác</SubmitButton>}
  </form>
}
