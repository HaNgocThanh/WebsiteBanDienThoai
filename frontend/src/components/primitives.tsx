import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode } from 'react'
import { useId } from 'react'
import { ApiError } from '../services/errors'

interface TextFieldProps extends InputHTMLAttributes<HTMLInputElement> { label: string; hint?: string; error?: string; visuallyHiddenLabel?: boolean }
export function TextField({ label, hint, error, id, visuallyHiddenLabel, ...props }: TextFieldProps) {
  const generated = useId()
  const inputId = id ?? generated
  const descriptions = [props['aria-describedby'], hint ? `${inputId}-hint` : undefined, error ? `${inputId}-error` : undefined].filter(Boolean).join(' ')
  return <div className="field">
    <label className={visuallyHiddenLabel ? 'sr-only' : undefined} htmlFor={inputId}>{label}</label>
    <input {...props} id={inputId} aria-invalid={error ? true : props['aria-invalid']} aria-describedby={descriptions || undefined} />
    {hint && <small id={`${inputId}-hint`} className="muted">{hint}</small>}
    {error && <small id={`${inputId}-error`} className="field-error" role="alert">{error}</small>}
  </div>
}

export function SubmitButton({ busy = false, children, disabled, ...props }: ButtonHTMLAttributes<HTMLButtonElement> & { busy?: boolean }) {
  return <button {...props} type={props.type ?? 'submit'} className={`button ${props.className ?? ''}`} disabled={disabled || busy} aria-busy={busy}>{children}</button>
}

export function EmptyState({ title, children, action }: { title: string; children: ReactNode; action?: ReactNode }) {
  return <section className="empty-state"><span className="empty-icon" aria-hidden="true">◇</span><h2>{title}</h2><div className="muted">{children}</div>{action}</section>
}

export function ErrorNotice({ error }: { error: ApiError }) {
  return <div className="notice notice-error" role="alert">
    <p>{error.message}</p>
    {Object.keys(error.errors).length > 0 && <ul>{Object.entries(error.errors).map(([field, messages]) => <li key={field}>{messages.join(' ')}</li>)}</ul>}
    {error.traceId && <small>Mã tham chiếu: {error.traceId}</small>}
  </div>
}
