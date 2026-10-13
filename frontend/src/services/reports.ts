import { parseEntityId, parseMoney, formatVietnamTime } from '../lib/contracts'
import { request } from './client'
import { isRecord } from './errors'
import { orderStatuses } from './orders'

export interface ReportDay { date: string; orders: number; orderValue: number; received: number; refunded: number; netReceived: number }
export interface OverviewReport {
  from: string; to: string; timezone: 'Asia/Ho_Chi_Minh'; generatedAt: string
  orders: number; orderValue: number; received: number; refunded: number; netReceived: number
  daily: ReportDay[]; statuses: { status: keyof typeof orderStatuses; count: number }[]
  bestSellers: { variantId: string; productName: string; variant: string; quantity: number; orders: number }[]
  queue: { reviewing: number; shipping: number; delivered: number; pendingRefunds: number }
  stock: { activeVariants: number; lowStockVariants: number; items: { variantId: string; productName: string; sku: string; available: number }[] }
}
function count(v: unknown): number { return parseMoney(v) }
function text(v: unknown): string { if (typeof v !== 'string') throw new TypeError('Invalid text'); return v }
export function reportDate(v: unknown) {
  const date = text(v)
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || Number.isNaN(Date.parse(date + 'T00:00:00Z')) || new Date(date + 'T00:00:00Z').toISOString().slice(0, 10) !== date) throw new TypeError('Invalid date')
  return date
}
function signed(v: unknown): number { if (typeof v !== 'number' || !Number.isSafeInteger(v)) throw new TypeError('Invalid signed VND'); return v }
export function formatSignedVnd(value: number) { return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(signed(value)) }
export function decodeOverview(v: unknown): OverviewReport {
  if (!isRecord(v) || v.timezone !== 'Asia/Ho_Chi_Minh' || !Array.isArray(v.daily) || !Array.isArray(v.statuses)
    || !Array.isArray(v.bestSellers) || v.bestSellers.length > 10 || !isRecord(v.queue) || !isRecord(v.stock) || !Array.isArray(v.stock.items) || v.stock.items.length > 10) throw new TypeError('Invalid report')
  const from = reportDate(v.from), to = reportDate(v.to), generatedAt = text(v.generatedAt); formatVietnamTime(generatedAt)
  const dayCount = (Date.parse(to) - Date.parse(from)) / 86400000 + 1
  if (dayCount < 1 || dayCount > 366 || v.daily.length !== dayCount) throw new TypeError('Invalid range')
  const daily = v.daily.map((d, index) => {
    if (!isRecord(d)) throw new TypeError('Invalid day')
    const day = { date: reportDate(d.date), orders: count(d.orders), orderValue: parseMoney(d.orderValue), received: parseMoney(d.received), refunded: parseMoney(d.refunded), netReceived: signed(d.netReceived) }
    if (Date.parse(day.date) !== Date.parse(from) + index * 86400000 || day.netReceived !== day.received - day.refunded) throw new TypeError('Invalid daily total')
    return day
  })
  const statuses = v.statuses.map(s => { if (!isRecord(s) || typeof s.status !== 'string' || !Object.hasOwn(orderStatuses, s.status)) throw new TypeError('Invalid status'); return { status: s.status as keyof typeof orderStatuses, count: count(s.count) } })
  if (statuses.length !== Object.keys(orderStatuses).length || new Set(statuses.map(s => s.status)).size !== statuses.length) throw new TypeError('Invalid status counts')
  const result: OverviewReport = {
    from, to, generatedAt, timezone: 'Asia/Ho_Chi_Minh', orders: count(v.orders), orderValue: parseMoney(v.orderValue), received: parseMoney(v.received), refunded: parseMoney(v.refunded), netReceived: signed(v.netReceived), daily, statuses,
    bestSellers: v.bestSellers.map(b => { if (!isRecord(b)) throw new TypeError('Invalid bestseller'); return { variantId: parseEntityId(b.variantId), productName: text(b.productName), variant: text(b.variant), quantity: count(b.quantity), orders: count(b.orders) } }),
    queue: { reviewing: count(v.queue.reviewing), shipping: count(v.queue.shipping), delivered: count(v.queue.delivered), pendingRefunds: count(v.queue.pendingRefunds) },
    stock: { activeVariants: count(v.stock.activeVariants), lowStockVariants: count(v.stock.lowStockVariants), items: v.stock.items.map(i => { if (!isRecord(i)) throw new TypeError('Invalid stock'); const available = count(i.available); if (available > 5) throw new TypeError('Not low stock'); return { variantId: parseEntityId(i.variantId), productName: text(i.productName), sku: text(i.sku), available } }) }
  }
  for (const field of ['orders', 'orderValue', 'received', 'refunded', 'netReceived'] as const)
    if (BigInt(result[field]) !== daily.reduce((sum, d) => sum + BigInt(d[field]), 0n)) throw new TypeError('Inconsistent totals')
  if (result.netReceived !== result.received - result.refunded || statuses.reduce((sum, s) => sum + s.count, 0) !== result.orders) throw new TypeError('Inconsistent report')
  return result
}
export const reports = { overview: (query: URLSearchParams, signal?: AbortSignal) => request('/api/v1/admin/reports/overview?' + query, decodeOverview, { signal }) }
