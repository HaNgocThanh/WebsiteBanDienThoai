import { useRef, useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { SubmitButton, TextField } from '../components/primitives'
import { formatVietnamTime, formatVnd } from '../lib/contracts'
import { ApiError } from '../services/errors'
import { payments, paymentStates } from '../services/payments'
import type { PaymentArea, CodDraft, PaymentPage, SePayCheckout } from '../services/payments'
import { ActionNotice, ResourceNotice, TextAreaField } from './AdminCatalogShared'
import { useMutation, useResource } from './adminCatalogHooks'
function uncertain(e: unknown) { return !(e instanceof ApiError) || e.status === 0 || e.status >= 500 || e.code === 'CHECKOUT_BUSY' || e.code === 'INVALID_RESPONSE' }
export function OrderPayments({ id, area, changed = () => {} }: { id: string; area: PaymentArea; changed?: () => void }) {
  const auth = useAuth(), [page, setPage] = useState(1), resource = useResource((auth.user?.userId ?? 'guest') + ':' + area + ':' + id + ':' + page, signal => payments.list(area, id, page, signal)), data = resource.data
  function reload() { resource.reload(); changed(); window.dispatchEvent(new Event('notifications-changed')) }
  return <section className="catalog-panel payment-panel"><h2>{area === 'admin' ? 'Đối soát thanh toán' : 'Thanh toán'}</h2>
    {!data ? <ResourceNotice error={resource.error} reload={resource.reload} loadingText="Đang tải thanh toán…" /> : <><p role="status">Trạng thái tiền: <strong>{paymentStates[data.summary.status]}</strong></p><dl className="payment-totals"><div><dt>Đã nhận</dt><dd>{formatVnd(data.summary.confirmed)}</dd></div><div><dt>Còn phải thu</dt><dd>{formatVnd(data.summary.remaining)}</dd></div><div><dt>Chờ hoàn</dt><dd>{formatVnd(data.summary.refundPending)}</dd></div><div><dt>Đã hoàn</dt><dd>{formatVnd(data.summary.refunded)}</dd></div></dl>
      {area !== 'admin' && data.method === 'BankTransfer' && <><p>SePay Sandbox dùng giao dịch thử nghiệm. Đơn chỉ được ghi nhận đã nhận tiền sau khi cửa hàng xác minh thông báo thanh toán.</p>{!data.sandboxConfigured && <p className="notice">SePay Sandbox chưa được cấu hình. Vui lòng liên hệ cửa hàng.</p>}{data.canPay && <GatewayForm key={area + ':' + id} id={id} area={area} configured={data.sandboxConfigured} />}</>}
      {data.canRecordCod && <CodForm key={id} id={id} data={data} reload={reload} />}
      {!data.items.length && <p>Chưa có khoản thanh toán.</p>}<ul className="catalog-list">{data.items.map(p => <li className="payment-row" key={p.id}><h3>{p.method === 'COD' ? 'Thu tiền khi nhận hàng' : 'Chuyển khoản'} · {formatVnd(p.amount)}</h3><p>{p.status === 'Pending' ? 'Đang chờ thanh toán' : p.status === 'Confirmed' ? 'Đã nhận tiền' : 'Đã từ chối'} · {formatVietnamTime(p.createdAt)}</p>{p.confirmedAt && <p>Xác nhận lúc {formatVietnamTime(p.confirmedAt)}</p>}{area === 'admin' && <>{p.reference && <p>Mã giao dịch: {p.reference}</p>}{p.note && <p>Ghi chú đối soát: {p.note}</p>}</>}</li>)}</ul>
      <SubmitButton type="button" onClick={reload}>Kiểm tra lại trạng thái tiền</SubmitButton><nav className="catalog-pagination" aria-label="Phân trang thanh toán"><SubmitButton type="button" disabled={page <= 1} onClick={() => setPage(n => n - 1)}>Khoản trước</SubmitButton><span>Trang {data.page} · {data.totalCount} khoản</span><SubmitButton type="button" disabled={data.page * data.pageSize >= data.totalCount} onClick={() => setPage(n => n + 1)}>Khoản sau</SubmitButton></nav>
    </>}
  </section>
}
function GatewayForm({ id, area, configured }: { id: string; area: PaymentArea; configured: boolean }) {
  const [form, setForm] = useState<SePayCheckout>(), action = useMutation()
  return <div>{!form ? <SubmitButton type="button" disabled={!configured} busy={action.busy} onClick={() => void action.run(signal => payments.checkout(area, id, signal), setForm, '')}>Thanh toán qua SePay Sandbox</SubmitButton> : <form method="post" action={form.action}>{form.fields.map(f => <input key={f.name} type="hidden" name={f.name} value={f.value} />)}<p>Số tiền thử nghiệm: {formatVnd(form.amount)} · Mã thanh toán {form.invoice}</p><SubmitButton>Tiếp tục sang SePay Sandbox</SubmitButton></form>}<ActionNotice action={action} /></div>
}
function CodForm({ id, data, reload }: { id: string; data: PaymentPage; reload: () => void }) {
  const [amount, setAmount] = useState(String(data.summary.remaining)), [note, setNote] = useState(''), [checked, setChecked] = useState(false), [attempt, setAttempt] = useState<CodDraft>(), action = useMutation(), lock = useRef(false)
  function submit() {
    if (lock.current || action.busy || !checked) return
    const current = attempt ?? { amount: Number(amount), note, operationKey: crypto.randomUUID() }; lock.current = true; setAttempt(current)
    void action.run(async signal => { try { return await payments.receipt(id, current, signal) } catch (e) { if (!uncertain(e)) setAttempt(undefined); throw e } }, () => { setAttempt(undefined); setNote(''); setChecked(false); reload() }, 'Đã ghi nhận khoản COD thực nhận.').finally(() => { lock.current = false })
  }
  return <form onSubmit={e => { e.preventDefault(); submit() }}><h3>Đối soát khoản COD đã thu</h3><fieldset className="checkout-fields" disabled={!!attempt || action.busy}><TextField label="Số tiền VND" type="number" inputMode="numeric" min={1} max={data.summary.remaining} step={1} required value={amount} onChange={e => setAmount(e.target.value)} /><TextAreaField label="Ghi chú đối soát" value={note} onChange={setNote} maxLength={500} required /><label className="checkout-consent"><input type="checkbox" checked={checked} onChange={e => setChecked(e.target.checked)} required /> Tôi đã kiểm tra và xác nhận số tiền thực nhận.</label></fieldset>{attempt && <p className="muted">Giữ nguyên nội dung để thử lại an toàn. Không tải lại trang khi chưa rõ kết quả.</p>}<SubmitButton busy={action.busy} disabled={!checked}>{attempt ? 'Thử lại cùng yêu cầu thanh toán' : 'Ghi nhận tiền đã nhận'}</SubmitButton><ActionNotice action={action} /></form>
}
