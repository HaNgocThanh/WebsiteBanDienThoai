import { useId } from 'react'
import type { ReactNode } from 'react'
import { ErrorNotice, SubmitButton } from '../components/primitives'
import { ApiError } from '../services/errors'
import type { useMutation } from './adminCatalogHooks'

export function ActionNotice({ action, refresh }: { action: ReturnType<typeof useMutation>; refresh?: () => void }) {
  return <>{action.error && <ErrorNotice error={action.error} />}{action.error?.status === 412 && refresh && <p><SubmitButton type="button" className="button-outline" onClick={refresh}>Tải phiên bản mới (bỏ thay đổi chưa lưu)</SubmitButton></p>}{action.success && <p className="notice" role="status">{action.success}</p>}</>
}
export function ResourceNotice({ error, reload, loadingText = 'Đang tải danh mục…' }: { error?: ApiError; reload: () => void; loadingText?: string }) {
  return error ? <><ErrorNotice error={error} /><SubmitButton type="button" onClick={reload}>Thử tải lại</SubmitButton></> : <p role="status">{loadingText}</p>
}
export function SelectField({ label, value, onChange, children, required = false, error }: { label: string; value: string; onChange: (value: string) => void; children: ReactNode; required?: boolean; error?: string }) {
  const id = useId()
  return <div className="field"><label htmlFor={id}>{label}</label><select id={id} value={value} onChange={e => onChange(e.target.value)} required={required} aria-invalid={error ? true : undefined} aria-describedby={error ? id + '-error' : undefined}>{children}</select>{error && <small className="field-error" id={id + '-error'}>{error}</small>}</div>
}
export function TextAreaField({ label, value, onChange, maxLength, hint, error }: { label: string; value: string; onChange: (value: string) => void; maxLength: number; hint?: string; error?: string }) {
  const id = useId()
  return <div className="field"><label htmlFor={id}>{label}</label><textarea id={id} value={value} onChange={e => onChange(e.target.value)} maxLength={maxLength} rows={4} aria-invalid={error ? true : undefined} aria-describedby={[hint ? id + '-hint' : '', error ? id + '-error' : ''].filter(Boolean).join(' ') || undefined} />{hint && <small id={id + '-hint'}>{hint}</small>}{error && <small className="field-error" id={id + '-error'}>{error}</small>}</div>
}
export function ActiveField({ value, onChange }: { value: boolean; onChange: (value: boolean) => void }) {
  return <label className="catalog-check"><input type="checkbox" checked={value} onChange={e => onChange(e.target.checked)} /> Đang hiển thị</label>
}
export function Status({ active }: { active: boolean }) { return <span className={`status-tag ${active ? '' : 'catalog-hidden'}`}>{active ? 'Hiển thị' : 'Đã ẩn'}</span> }
