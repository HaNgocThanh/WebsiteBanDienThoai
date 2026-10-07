export interface HealthResponse {
  status: string
  message: string
}

export async function getApiHealth(): Promise<HealthResponse> {
  const response = await fetch('/api/health')

  if (!response.ok) {
    throw new Error(`API trả lỗi ${response.status}`)
  }

  return response.json()
}