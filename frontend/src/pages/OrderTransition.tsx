import { useRef, useState } from 'react'
import { SubmitButton } from '../components/primitives'
import { orderStatuses } from '../services/orders'
import { orderManagement } from '../services/orderManagement'
import type { OrderDetail } from '../services/orderManagement'
import { ApiError } from '../services/errors'
import { ActionNotice, TextAreaField } from './AdminCatalogShared'
import { useMutation } from './adminCatalogHooks'
const reasonOptions: Record<string, string[]> = {
  Reviewing: ['Đã kiểm tra và giữ đủ hàng cho đơn'],
  Approved: ['Đã kiểm tra thông tin đơn và xác nhận xử lý', 'Đã liên hệ khách hàng xác nhận đơn'],
  Shipping: ['Đã đóng gói và bàn giao cho đơn vị vận chuyển', 'Đã bàn giao cho nhân viên giao hàng'],
  Delivered: ['Đơn vị vận chuyển xác nhận đã giao thành công', 'Khách hàng xác nhận đã nhận hàng'],
  Completed: ['Đã giao hàng, nhận đủ tiền và đối soát hoàn tất'],
}
type Attempt = { targetStatus: string; reason: string; operationKey: string; version: string }
export function OrderTransition({ id, detail, reload }: { id: string; detail: OrderDetail; reload: () => void }) {
  const [selectedReason, setSelectedReason] = useState(''), [note, setNote] = useState(''), [confirmed, setConfirmed] = useState(false), [needsRefresh, setNeedsRefresh] = useState(false), [attempt, setAttempt] = useState<Attempt>(), action = useMutation(), lock = useRef(false)
  const target = attempt?.targetStatus ?? detail.allowedActions[0]
  const options = reasonOptions[target] ?? [], reason = selectedReason + (note.trim() ? ' — ' + note.trim() : '')
  function submit() {
    if (lock.current || action.busy || needsRefresh || !confirmed || !target || !options.includes(selectedReason)) return
    const current = attempt ?? { targetStatus: target, reason: reason.trim(), operationKey: crypto.randomUUID(), version: detail.version }
    lock.current = true; setAttempt(current)
    void action.run(async signal => { try { return await orderManagement.transition(id, current, signal) } catch (e) {
      if (e instanceof ApiError && e.status > 0 && e.status < 500 && e.code !== 'CHECKOUT_BUSY' && e.code !== 'INVALID_RESPONSE') { setAttempt(undefined); setConfirmed(false); setNeedsRefresh(e.status === 409 || e.status === 412) }
      throw e
    } }, () => { setAttempt(undefined); setSelectedReason(''); setNote(''); setConfirmed(false); reload(); window.dispatchEvent(new Event('notifications-changed')) }, 'Đã cập nhật trạng thái đơn.').finally(() => { lock.current = false })
  }
  if (!target) return <section className="catalog-panel"><h2>Xử lý đơn hàng</h2><p>Chưa có bước chuyển hợp lệ. Kiểm tra trạng thái đơn và thanh toán.</p></section>
  const label = orderStatuses[target as keyof typeof orderStatuses]
  return <section className="catalog-panel"><h2>Xử lý đơn hàng</h2><p>Bước tiếp theo: <strong>{label}</strong></p>{target === 'Shipping' && <p>Xác nhận xuất toàn bộ hàng đã giữ cho đơn và bàn giao vận chuyển.</p>}{target === 'Completed' && <p>Chỉ hoàn tất khi đã nhận đủ tiền và không còn khoản hoàn đang chờ.</p>}<form onSubmit={e => { e.preventDefault(); submit() }}><fieldset className="checkout-fields" disabled={!!attempt || action.busy || needsRefresh}><label className="field">Lý do cập nhật trạng thái<select required value={selectedReason} onChange={e => setSelectedReason(e.target.value)}><option value="">Chọn lý do</option>{options.map(value => <option key={value} value={value}>{value}</option>)}</select></label><TextAreaField label="Ghi chú bổ sung (không bắt buộc)" value={note} onChange={setNote} maxLength={Math.max(0, 300 - selectedReason.length - 3)} /><label className="checkout-consent"><input type="checkbox" checked={confirmed} onChange={e => setConfirmed(e.target.checked)} required /> Tôi đã kiểm tra và xác nhận bước xử lý này.</label></fieldset>{attempt && <p className="muted">Giữ nguyên nội dung và thử lại để tránh xử lý trùng khi chưa rõ kết quả.</p>}<SubmitButton busy={action.busy} disabled={needsRefresh || !confirmed || !options.includes(selectedReason)}>{attempt ? 'Thử lại cùng bước xử lý' : 'Chuyển sang ' + label}</SubmitButton></form><ActionNotice action={action} />{needsRefresh && <SubmitButton type="button" onClick={reload}>Tải lại trạng thái đơn</SubmitButton>}</section>
}
