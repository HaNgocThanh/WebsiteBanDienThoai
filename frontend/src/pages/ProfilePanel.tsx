import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useAuth } from '../auth/AuthContext'
import { ErrorNotice, TextField } from '../components/primitives'
import { ApiError } from '../services/errors'
import { deleteAddress, getAddresses, getProfile, saveAddress, updateProfile } from '../services/profile'
import type { Address, AddressInput, Profile } from '../services/profile'

const blank: AddressInput = { recipientName: '', phone: '', addressLine: '', locality: '', province: '', countryCode: 'VN', isDefault: false }
const apiError = (value: unknown) => value instanceof ApiError ? value : new ApiError(0, 'NETWORK_ERROR')
export function ProfilePanel() {
  const auth = useAuth()
  const [profile, setProfile] = useState<Profile>()
  const [addresses, setAddresses] = useState<Address[]>([])
  const [fullName, setFullName] = useState(''); const [phone, setPhone] = useState('')
  const [draft, setDraft] = useState<AddressInput>(blank); const [editing, setEditing] = useState<string>()
  const [deleting, setDeleting] = useState<string>()
  const [error, setError] = useState<ApiError>(); const [success, setSuccess] = useState('')
  const [busy, setBusy] = useState(false); const [attempt, setAttempt] = useState(0)
  const lock = useRef(false); const operation = useRef<AbortController | null>(null)
  useEffect(() => {
    const abort = new AbortController()
    Promise.all([getProfile(abort.signal), getAddresses(abort.signal)]).then(([next, list]) => {
      if (abort.signal.aborted) return
      setProfile(next); setFullName(next.fullName); setPhone(next.phone ?? ''); setAddresses(list); setError(undefined)
    }).catch(failure => { if (!abort.signal.aborted) setError(apiError(failure)) })
    return () => { abort.abort(); operation.current?.abort() }
  }, [attempt])
  async function run(action: (signal: AbortSignal) => Promise<void>, message: string) {
    if (lock.current) return
    lock.current = true; setBusy(true); setError(undefined); setSuccess('')
    const abort = new AbortController(); operation.current = abort
    try { await action(abort.signal); if (!abort.signal.aborted) setSuccess(message) }
    catch (failure) { if (!abort.signal.aborted) setError(apiError(failure)) }
    finally { lock.current = false; if (!abort.signal.aborted) setBusy(false) }
  }
  function saveProfile(event: FormEvent) {
    event.preventDefault()
    void run(async signal => {
      await updateProfile({ fullName: fullName.trim(), phone: phone.trim() || null }, signal)
      const next = await getProfile(signal)
      if (signal.aborted) return
      setProfile(next); await auth.refresh()
    }, 'Hồ sơ đã được cập nhật.')
  }
  function submitAddress(event: FormEvent) {
    event.preventDefault()
    void run(async signal => {
      const saved = await saveAddress(editing, { ...draft, locality: draft.locality?.trim() || null }, signal)
      if (signal.aborted) return
      setAddresses(current => [...current.filter(item => item.id !== saved.id).map(item => saved.isDefault ? { ...item, isDefault: false } : item), saved])
      setDraft(blank); setEditing(undefined)
    }, 'Địa chỉ đã được lưu.')
  }
  return <div className="profile-panel">
    {error && <ErrorNotice error={error} />}{success && <p className="notice notice-success" role="status">{success}</p>}
    {!profile ? error ? <button className="button button-outline" onClick={() => setAttempt(value => value + 1)}>Tải lại hồ sơ</button> : <p role="status">Đang tải hồ sơ…</p> : <>
      <div className="health-card"><h2>Hồ sơ của bạn</h2>
        <form className="auth-form" onSubmit={saveProfile}><fieldset disabled={busy}>
          <TextField label="Email tài khoản" value={profile.email} readOnly hint="Email đã xác minh và không thể thay đổi." />
          <TextField label="Họ tên" value={fullName} onChange={event => setFullName(event.target.value)} required maxLength={150} />
          <TextField label="Số điện thoại hồ sơ" value={phone} onChange={event => setPhone(event.target.value)} type="tel" maxLength={30} />
          <p>Hạng: {profile.tierCode ?? 'Chưa có'} · Chi tiêu hợp lệ: {profile.eligibleSpend.toLocaleString('vi-VN')} ₫</p>
          <button className="button" type="submit">Lưu hồ sơ</button>
        </fieldset></form>
      </div>
      <div className="health-card"><h2>Địa chỉ nhận hàng</h2><p className="muted">Bạn có thể chọn một địa chỉ mặc định. Xóa địa chỉ không thay đổi thông tin trên đơn đã đặt.</p>
        {addresses.length === 0 && <p>Chưa có địa chỉ.</p>}
        <ul className="address-list">{addresses.map(item => <li key={item.id}><strong>{item.recipientName}</strong>{item.isDefault && <span> · Mặc định</span>}<p>{item.phone} · {item.addressLine}, {item.locality && item.locality + ', '}{item.province}, {item.countryCode}</p>
          <button className="button button-outline" disabled={busy} onClick={() => { setEditing(item.id); setDraft({ recipientName: item.recipientName, phone: item.phone, addressLine: item.addressLine, locality: item.locality, province: item.province, countryCode: item.countryCode, isDefault: item.isDefault }); setSuccess(''); setDeleting(undefined) }}>Sửa {item.recipientName}</button>{' '}
          <button className="button button-outline" disabled={busy} onClick={() => setDeleting(item.id)}>Xóa {item.recipientName}</button>
          {deleting === item.id && <div role="group" aria-label="Xác nhận xóa địa chỉ"><p>Xóa địa chỉ này?</p><button className="button" disabled={busy} onClick={() => { void run(async signal => { await deleteAddress(item.id, signal); if (!signal.aborted) { setAddresses(current => current.filter(address => address.id !== item.id)); setDeleting(undefined); if (editing === item.id) { setEditing(undefined); setDraft(blank) } } }, 'Địa chỉ đã được xóa.') }}>Xác nhận xóa</button>{' '}<button className="button button-outline" disabled={busy} onClick={() => setDeleting(undefined)}>Giữ địa chỉ</button></div>}
        </li>)}</ul>
        <h3>{editing ? 'Sửa địa chỉ' : 'Thêm địa chỉ'}</h3><form className="auth-form" onSubmit={submitAddress}><fieldset disabled={busy}>
          <TextField label="Người nhận" required maxLength={150} value={draft.recipientName} onChange={event => setDraft({ ...draft, recipientName: event.target.value })} />
          <TextField label="Điện thoại người nhận" required type="tel" maxLength={30} value={draft.phone} onChange={event => setDraft({ ...draft, phone: event.target.value })} />
          <TextField label="Số nhà, đường" required maxLength={300} value={draft.addressLine} onChange={event => setDraft({ ...draft, addressLine: event.target.value })} />
          <TextField label="Phường, xã" maxLength={150} value={draft.locality ?? ''} onChange={event => setDraft({ ...draft, locality: event.target.value })} />
          <TextField label="Tỉnh, thành phố" required maxLength={150} value={draft.province} onChange={event => setDraft({ ...draft, province: event.target.value })} />
          <TextField label="Mã quốc gia" required maxLength={2} pattern="[A-Za-z]{2}" value={draft.countryCode} onChange={event => setDraft({ ...draft, countryCode: event.target.value })} />
          <label><input type="checkbox" checked={draft.isDefault} onChange={event => setDraft({ ...draft, isDefault: event.target.checked })} /> Đặt làm mặc định</label>
          <button className="button" type="submit">Lưu địa chỉ</button>{editing && <button className="button button-outline" type="button" onClick={() => { setEditing(undefined); setDraft(blank) }}>Hủy sửa</button>}
        </fieldset></form>
      </div>
    </>}
  </div>
}
