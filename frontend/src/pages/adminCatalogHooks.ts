import { useEffect, useEffectEvent, useRef, useState } from 'react'
import { ApiError } from '../services/errors'

export function useResource<T>(key: string, loader: (signal: AbortSignal) => Promise<T>) {
  const load = useEffectEvent(loader)
  const [revision, setRevision] = useState(0)
  const [state, setState] = useState<{ key: string; data?: T; error?: ApiError }>({ key })
  useEffect(() => {
    const controller = new AbortController()
    const stateKey = key + ":" + revision
    void Promise.resolve().then(() => load(controller.signal)).then(data => { if (!controller.signal.aborted) setState({ key: stateKey, data }) }).catch((e: unknown) => { if (!controller.signal.aborted) setState({ key: stateKey, error: e instanceof ApiError ? e : new ApiError(0, 'INVALID_RESPONSE') }) })
    return () => controller.abort()
  }, [key, revision])
  return { ...(state.key === key + ":" + revision ? state : { key }), reload: () => setRevision(x => x + 1) }
}
export interface MutationGate { acquire: () => boolean; release: () => void }
export function useMutation(gate?: MutationGate) {
  const pending = useRef(false)
  const controller = useRef<AbortController | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<ApiError>()
  const [success, setSuccess] = useState('')
  useEffect(() => () => controller.current?.abort(), [])
  async function run<T>(action: (signal: AbortSignal) => Promise<T>, done: (data: T) => void, message: string) {
    if (pending.current || (gate && !gate.acquire())) return
    pending.current = true; setBusy(true); setError(undefined); setSuccess('')
    const next = new AbortController(); controller.current = next
    try { const result = await action(next.signal); if (!next.signal.aborted) { done(result); setSuccess(message) } }
    catch (e) { if (!next.signal.aborted) setError(e instanceof ApiError ? e : new ApiError(0, 'INVALID_RESPONSE')) }
    finally { pending.current = false;  if (!next.signal.aborted) { setBusy(false); gate?.release() } }
  }
  return { busy, error, success, run, clear: () => { setError(undefined); setSuccess('') } }
}
