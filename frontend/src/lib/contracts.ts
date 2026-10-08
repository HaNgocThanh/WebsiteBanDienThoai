import type { EntityId, PagedResponse } from '../types/contracts'
import { isRecord } from '../services/errors'
import type { Decoder } from '../services/client'

export function parseEntityId(value: unknown): EntityId {
  if (typeof value !== 'string' || !/^[1-9]\d{0,18}$/.test(value) || BigInt(value) > 9223372036854775807n) {
    throw new TypeError('Expected a SQL bigint ID string.')
  }
  return value
}

export function parseMoney(value: unknown): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0) throw new TypeError('Expected safe integer VND.')
  return value
}

export function formatVnd(value: number): string {
  return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(parseMoney(value))
}

export function formatVietnamTime(value: string): string {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?Z$/.test(value) || Number.isNaN(Date.parse(value))
    || new Date(value).toISOString().slice(0, 19) !== value.slice(0, 19)) {
    throw new TypeError('Expected an ISO UTC timestamp.')
  }
  return new Intl.DateTimeFormat('vi-VN', {
    timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
  }).format(new Date(value))
}

export function pagedDecoder<T>(item: Decoder<T>): Decoder<PagedResponse<T>> {
  return value => {
    if (!isRecord(value) || !Array.isArray(value.items)
      || typeof value.page !== 'number' || !Number.isSafeInteger(value.page) || value.page < 1
      || typeof value.pageSize !== 'number' || !Number.isSafeInteger(value.pageSize) || value.pageSize < 1 || value.pageSize > 100
      || typeof value.totalCount !== 'number' || !Number.isSafeInteger(value.totalCount) || value.totalCount < 0
      || value.items.length > value.pageSize || value.items.length > value.totalCount) throw new TypeError('Invalid page response.')
    return { items: value.items.map(item), page: value.page, pageSize: value.pageSize, totalCount: value.totalCount }
  }
}
