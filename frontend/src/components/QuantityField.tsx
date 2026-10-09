import { useId, useRef } from 'react'

export function QuantityField({ label, accessibleLabel, value, onChange, min = 1, max = 2147483647, hint, error, required = true }: {
  label: string; accessibleLabel?: string; value: string; onChange: (value: string) => void; min?: number; max?: number; hint?: string; error?: string; required?: boolean
}) {
  const id = useId(), input = useRef<HTMLInputElement>(null)
  const parsed = /^-?\d+$/.test(value) && Number.isSafeInteger(Number(value)) ? Number(value) : undefined
  function step(delta: number) {
    const next = Math.min(max, Math.max(min, (parsed ?? (min > 0 ? min - 1 : 0)) + delta))
    onChange(String(next)); input.current?.focus()
  }
  return <div className="field"><label htmlFor={id}>{label}</label><div className="quantity-control">
    <input ref={input} id={id} role="spinbutton" aria-label={accessibleLabel} inputMode={min < 0 ? 'text' : 'numeric'} required={required} value={value}
      aria-valuemin={min} aria-valuemax={max} aria-valuenow={parsed} aria-invalid={!!error} aria-describedby={hint || error ? id + '-hint' : undefined}
      onChange={e => onChange(e.target.value)} onKeyDown={e => { if (e.key === 'ArrowUp' || e.key === 'ArrowDown') { e.preventDefault(); step(e.key === 'ArrowUp' ? 1 : -1) } }} />
    <div className="quantity-buttons"><button type="button" aria-label={`Tăng ${(accessibleLabel ?? label).toLocaleLowerCase('vi-VN')}`} disabled={parsed !== undefined && parsed >= max} onClick={() => step(1)}>▴</button><button type="button" aria-label={`Giảm ${(accessibleLabel ?? label).toLocaleLowerCase('vi-VN')}`} disabled={parsed !== undefined && parsed <= min} onClick={() => step(-1)}>▾</button></div>
  </div>{(hint || error) && <small id={id + '-hint'} className={error ? 'field-error' : 'muted'} role={error ? 'alert' : undefined}>{error ?? hint}</small>}</div>
}
