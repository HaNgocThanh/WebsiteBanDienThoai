import { useEffect, useId, useState } from 'react'
import { locations } from '../services/locations'
import type { LocationList } from '../services/locations'
import type { Destination } from '../services/locations'
import { ApiError } from '../services/errors'
import { ErrorNotice, TextField } from './primitives'

export function AddressFields({ value, onChange }: { value: Destination; onChange: (value: Destination) => void }) {
  const id = useId()
  const [provinces, setProvinces] = useState<LocationList>()
  const [wardList, setWardList] = useState<{ code: string; list: LocationList }>()
  const [error, setError] = useState<ApiError>()
  const [attempt, setAttempt] = useState(0)
  useEffect(() => {
    const abort = new AbortController()
    locations.provinces(abort.signal).then(list => { if (!abort.signal.aborted) { setProvinces(list); setError(undefined) } }).catch(e => { if (!abort.signal.aborted) setError(e instanceof ApiError ? e : new ApiError(0, 'NETWORK_ERROR')) })
    return () => abort.abort()
  }, [attempt])
  useEffect(() => {
    if (!value.provinceCode) return
    const abort = new AbortController(), code = value.provinceCode
    locations.wards(code, abort.signal).then(list => { if (!abort.signal.aborted) { setWardList({ code, list }); setError(undefined) } }).catch(e => { if (!abort.signal.aborted) setError(e instanceof ApiError ? e : new ApiError(0, 'NETWORK_ERROR')) })
    return () => abort.abort()
  }, [value.provinceCode, attempt])
  const wards = wardList?.code === value.provinceCode ? wardList.list : undefined
  return <div className="address-fields">
    <TextField label="Số nhà, đường" value={value.addressLine} required maxLength={300} onChange={e => onChange({ ...value, addressLine: e.target.value })} />
    <div className="field"><label htmlFor={id + '-province'}>Tỉnh/thành phố</label><select id={id + '-province'} value={value.provinceCode} required disabled={!provinces} onChange={e => { const p = provinces?.items.find(p => p.code === e.target.value); onChange({ ...value, provinceCode: p?.code ?? '', province: p?.name ?? '', wardCode: '', locality: '' }) }}><option value="">{provinces ? 'Chọn tỉnh/thành phố' : 'Đang tải tỉnh/thành phố…'}</option>{provinces?.items.map(p => <option key={p.code} value={p.code}>{p.name}</option>)}</select></div>
    {value.provinceCode && <div className="field"><label htmlFor={id + '-ward'}>Phường/xã</label><select id={id + '-ward'} value={value.wardCode} required disabled={!wards} onChange={e => { const w = wards?.items.find(w => w.code === e.target.value); onChange({ ...value, wardCode: w?.code ?? '', locality: w?.name ?? '' }) }}><option value="">{wards ? 'Chọn phường/xã' : 'Đang tải phường/xã…'}</option>{wards?.items.map(w => <option key={w.code} value={w.code}>{w.name}</option>)}</select></div>}
    {provinces && <small className="muted">Danh mục hành chính ngày {provinces.asOf.split('-').reverse().join('/')} · Việt Nam</small>}
    {error && <><ErrorNotice error={error} /><button type="button" className="button button-outline" onClick={() => setAttempt(n => n + 1)}>Tải lại danh mục địa chỉ</button></>}
  </div>
}
