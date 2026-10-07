// IDs are opaque strings. Never convert SQL bigint IDs to JavaScript numbers.
export type EntityId = string
export type UtcTimestamp = string
export type RowVersion = string
export interface PagedResponse<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}
export interface ApiProblem {
  type: string
  title: string
  status: number
  code: string
  traceId: string
  errors?: Record<string, string[]>
}
