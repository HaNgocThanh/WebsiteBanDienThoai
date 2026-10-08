import { request } from './client'
import { isRecord } from './errors'

export interface HealthResponse { status: 'ok'; message: string }
export function getApiHealth(signal?: AbortSignal): Promise<HealthResponse> {
  return request('/api/health', value => {
    if (!isRecord(value) || value.status !== 'ok' || typeof value.message !== 'string' || !value.message.trim()) {
      throw new TypeError('Invalid health response.')
    }
    return { status: 'ok', message: value.message }
  }, { signal })
}
