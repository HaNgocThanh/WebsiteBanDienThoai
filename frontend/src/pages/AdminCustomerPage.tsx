import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { ErrorNotice, SubmitButton, TextField } from '../components/primitives'
import { ApiError } from '../services/errors'
import { adminCustomers } from '../services/adminCustomers'
import type { AdminCustomerProfile, CustomerUpdate } from '../services/adminCustomers'
import type { Address } from '../services/profile'
import { MembershipPage } from './MembershipPage'
const failure = (v: unknown) => v instanceof ApiError ? v : new ApiError(0, 'NETWORK_ERROR')

export function AdminCustomerPage() {
  const { userId = '' } = useParams()
  return <AdminCustomerEditor key={userId} userId={userId} />
}
function AdminCustomerEditor({ userId }: { userId: string }) {
  const [profile, setProfile] = useState<AdminCustomerProfile>(), [addresses, setAddresses] = useState<Address[]>([]), [name, setName] = useState(''), [phone, setPhone] = useState(''), [active, setActive] = useState(true), [error, setError] = useState<ApiError>(), [busy, setBusy] = useState(false), [pending, setPending] = useState(false), [success, setSuccess] = useState(''), [retry, setRetry] = useState(0)
  const operation = useRef<CustomerUpdate | null>(null), lock = useRef(false), controller = useRef<AbortController | null>(null)
  useEffect(() => {
    const abort = new AbortController()
    Promise.all([adminCustomers.profile(userId, abort.signal), adminCustomers.addresses(userId, abort.signal)]).then(([p, a]) => { if (!abort.signal.aborted) { setProfile(p); setName(p.fullName); setPhone(p.phone ?? ''); setActive(p.isActive); setAddresses(a); setError(undefined) } }).catch(e => { if (!abort.signal.aborted) setError(failure(e)) })
    return () => { abort.abort(); controller.current?.abort() }
  }, [userId, retry])
  async function submit(e: FormEvent) {
    e.preventDefault(); if (lock.current || !profile || !name.trim()) return
    operation.current ??= { fullName: name.trim(), phone: phone.trim() || null, isActive: active, version: profile.version, operationKey: crypto.randomUUID() }
    lock.current = true; setBusy(true); setPending(true); setError(undefined); setSuccess('')
    const abort = new AbortController(); controller.current = abort
    try {
      await adminCustomers.update(userId, operation.current, abort.signal)
      const fresh = await adminCustomers.profile(userId, abort.signal)
      if (!abort.signal.aborted) { setProfile(fresh); setName(fresh.fullName); setPhone(fresh.phone ?? ''); setActive(fresh.isActive); operation.current = null; setPending(false); setSuccess('Đã cập nhật thông tin khách hàng.') }
    } catch (e) { if (!abort.signal.aborted) { const err = failure(e); setError(err); if (err.status !== 0 && err.status < 500) { operation.current = null; setPending(false) } } }
    finally { lock.current = false; if (!abort.signal.aborted) setBusy(false) }
  }
  return <section className="page-section membership-page"><Link to="/admin/customers">← Danh sách khách hàng</Link><h1>Chi tiết khách hàng</h1>{error && <ErrorNotice error={error} />}{success && <p role="status">{success}</p>}
    {!profile && !error && <p role="status">Đang tải hồ sơ…</p>}
    {profile && <><h2>Thông tin khách hàng</h2><form onSubmit={e => void submit(e)}><fieldset disabled={busy || pending}><legend>Hỗ trợ cập nhật hồ sơ</legend><TextField label="Email khách hàng" readOnly value={profile.email} /><p>Email: {profile.emailConfirmed ? 'Đã xác minh' : 'Chưa xác minh'}</p>
      <TextField label="Họ tên khách hàng" value={name} onChange={e => setName(e.target.value)} required maxLength={150} /><TextField label="Số điện thoại khách hàng" type="tel" value={phone} onChange={e => setPhone(e.target.value)} maxLength={30} />
      <label><input type="checkbox" checked={active} disabled={!profile.canChangeStatus} onChange={e => setActive(e.target.checked)} /> Tài khoản hoạt động</label>{!active && profile.canChangeStatus && <p>Khách hàng sẽ không thể đăng nhập khi bạn lưu trạng thái khóa.</p>}
    </fieldset>{pending && !busy && <p>Chưa xác định kết quả. Thử lại cùng thao tác để lấy kết quả đã lưu.</p>}<SubmitButton busy={busy}>{pending ? 'Thử cập nhật lại' : 'Lưu thông tin khách hàng'}</SubmitButton></form>
      <h2>Địa chỉ đã lưu</h2>{addresses.length === 0 ? <p>Khách chưa lưu địa chỉ.</p> : <ul className="membership-ledger">{addresses.map(a => <li key={a.id}><strong>{a.recipientName}</strong>{a.isDefault && ' · Mặc định'}<p>{a.phone} · {a.addressLine}, {a.locality && a.locality + ', '}{a.province}</p></li>)}</ul>}
      <Link className="button button-outline" to={'/admin/orders?customerId=' + encodeURIComponent(userId)}>Xem đơn hàng của khách</Link><MembershipPage key={profile.version} admin /></>}
    <button type="button" disabled={busy || pending} onClick={() => { setSuccess(''); setRetry(n => n + 1) }}>Tải lại hồ sơ</button>
  </section>
}
