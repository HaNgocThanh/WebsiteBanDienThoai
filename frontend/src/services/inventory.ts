import { parseEntityId, pagedDecoder } from '../lib/contracts'
import { parseVersion } from './catalog'
import { request } from './client'
import { ApiError, isRecord } from './errors'

export interface Balance { variantId: string; productId: string; productName: string; sku: string; color: string; storageGb: number; ramGb: number; isActive: boolean; onHand: number; reserved: number; available: number; version: string }
export interface Movement { id: string; variantId: string; kind: 'Receive' | 'Adjust' | 'Reserve' | 'Release' | 'Dispatch' | 'CancelReturn'; onHandDelta: number; reservedDelta: number; reason: string; actorUserId: string | null; createdAt: string; operationKey: string | null }
export interface StockCommand { variantId: string; kind: 'receive' | 'adjust'; delta: number; reason: string; operationKey: string }
export interface StockResult { inventory: Balance; movement: Movement; isReplay: boolean }
const uuid = /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/
function text(v: unknown): string { if (typeof v !== 'string') throw new TypeError('Invalid text'); return v }
function integer(v: unknown, min = 0, max = 2147483647): number { if (typeof v !== 'number' || !Number.isInteger(v) || v < min || v > max) throw new TypeError('Invalid quantity'); return v }
export function decodeBalance(value: unknown): Balance {
  if (!isRecord(value) || typeof value.isActive !== 'boolean') throw new TypeError('Invalid balance')
  const onHand = integer(value.onHand), reserved = integer(value.reserved), available = integer(value.available)
  if (reserved > onHand || available !== onHand - reserved) throw new TypeError('Invalid balance invariant')
  return { variantId: parseEntityId(value.variantId), productId: parseEntityId(value.productId), productName: text(value.productName), sku: text(value.sku), color: text(value.color), storageGb: integer(value.storageGb, 1, 65536), ramGb: integer(value.ramGb, 1, 1024), isActive: value.isActive, onHand, reserved, available, version: parseVersion(value.version) }
}
export function decodeMovement(value: unknown): Movement {
  if (!isRecord(value) || !['Receive', 'Adjust', 'Reserve', 'Release', 'Dispatch', 'CancelReturn'].includes(text(value.kind))) throw new TypeError('Invalid movement')
  const createdAt = text(value.createdAt)
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?Z$/.test(createdAt) || Number.isNaN(Date.parse(createdAt)) || new Date(createdAt).toISOString().slice(0, 19) !== createdAt.slice(0, 19)) throw new TypeError('Invalid UTC timestamp')
  const actorUserId = value.actorUserId === null ? null : text(value.actorUserId)
  const operationKey = value.operationKey === null ? null : text(value.operationKey)
  if ((actorUserId !== null && !uuid.test(actorUserId)) || (operationKey !== null && !uuid.test(operationKey))) throw new TypeError('Invalid operation/actor')
  return { id: parseEntityId(value.id), variantId: parseEntityId(value.variantId), kind: value.kind as Movement['kind'], onHandDelta: integer(value.onHandDelta, -2147483648), reservedDelta: integer(value.reservedDelta, -2147483648), reason: text(value.reason), actorUserId, createdAt, operationKey }
}
export function decodeStockResult(value: unknown): StockResult {
  if (!isRecord(value) || typeof value.isReplay !== 'boolean') throw new TypeError('Invalid command result')
  const inventory = decodeBalance(value.inventory), movement = decodeMovement(value.movement)
  if (inventory.variantId !== movement.variantId || movement.reservedDelta !== 0 || !['Receive', 'Adjust'].includes(movement.kind)) throw new TypeError('Invalid command relationship')
  return { inventory, movement, isReplay: value.isReplay }
}
export function decodeCommand(value: unknown): StockCommand {
  if (!isRecord(value) || (value.kind !== 'receive' && value.kind !== 'adjust') || !uuid.test(text(value.operationKey)) || value.operationKey === '00000000-0000-0000-0000-000000000000') throw new TypeError('Invalid command')
  const delta = integer(value.delta, value.kind === 'receive' ? 1 : -2147483648), reason = text(value.reason)
  if (delta === 0 || !reason.trim() || reason.length > 300) throw new TypeError('Invalid command payload')
  return { variantId: parseEntityId(value.variantId), kind: value.kind, delta, reason, operationKey: text(value.operationKey) }
}
const root = '/api/v1/admin/inventory'
export const inventoryApi = {
  list: (query: URLSearchParams, signal?: AbortSignal) => request(`${root}?${query}`, pagedDecoder(decodeBalance), { signal }),
  detail: (id: string, signal?: AbortSignal) => request(`${root}/${parseEntityId(id)}`, decodeBalance, { signal }),
  movements: (id: string, page: number, signal?: AbortSignal) => request(`${root}/${parseEntityId(id)}/movements?page=${page}&pageSize=10`, pagedDecoder(decodeMovement), { signal }),
  write: (input: StockCommand, signal?: AbortSignal) => {
    const command = decodeCommand(input)
    const body = { reason: command.reason, operationKey: command.operationKey, ...(command.kind === 'receive' ? { quantity: command.delta } : { quantityDelta: command.delta }) }
    return request(`${root}/${command.variantId}/${command.kind === 'receive' ? 'receipts' : 'adjustments'}`, value => {
      const result = decodeStockResult(value)
      if (result.inventory.variantId !== command.variantId || result.movement.operationKey !== command.operationKey || result.movement.onHandDelta !== command.delta
        || result.movement.kind !== (command.kind === 'receive' ? 'Receive' : 'Adjust') || result.movement.reason !== command.reason.trim().normalize('NFC')) throw new TypeError('Response does not match submitted command')
      return result
    }, { method: 'POST', body, signal })
  },
}
// Tab-scoped pending commands only: quantity/reason/key, never auth tokens or profile fields.
export function pendingKey(actor: string, variant: string) { if (!uuid.test(actor)) throw new TypeError('Invalid actor'); return `phonestore.inventory.${actor}.${parseEntityId(variant)}` }
export function readPending(actor: string, variant: string): StockCommand | undefined {
  try { const raw = sessionStorage.getItem(pendingKey(actor, variant)); if (!raw) return; const command = decodeCommand(JSON.parse(raw)); if (command.variantId !== variant) throw new TypeError('Wrong variant'); return command }
  catch { throw new ApiError(0, 'OPERATION_STORAGE_UNAVAILABLE') }
}
export function storePending(actor: string, command: StockCommand) { try { sessionStorage.setItem(pendingKey(actor, command.variantId), JSON.stringify(decodeCommand(command))) } catch { throw new ApiError(0, 'OPERATION_STORAGE_UNAVAILABLE') } }
export function clearPending(actor: string, variant: string) { try { sessionStorage.removeItem(pendingKey(actor, variant)) } catch { throw new ApiError(0, 'OPERATION_STORAGE_UNAVAILABLE') } }
