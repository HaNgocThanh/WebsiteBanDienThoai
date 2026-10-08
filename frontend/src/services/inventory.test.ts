import { afterEach, expect, test, vi } from 'vitest'
import { decodeBalance, decodeCommand, decodeMovement, decodeStockResult, inventoryApi, readPending, storePending } from './inventory'

const balance = { variantId: '9007199254740993', productId: '1', productName: 'Synthetic', sku: 'TEST', color: 'Black', storageGb: 128, ramGb: 8, isActive: true, onHand: 10, reserved: 8, available: 2, version: 'AAAAAAAAAAE=' }
const movement = { id: '1', variantId: balance.variantId, kind: 'Receive', onHandDelta: 10, reservedDelta: 0, reason: 'Synthetic', actorUserId: null, createdAt: '2026-10-08T00:00:00.000Z', operationKey: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' }
afterEach(() => { sessionStorage.clear(); vi.unstubAllGlobals() })
test('inventory DTO preserves bigint strings and rejects invalid stock invariants', () => {
  expect(decodeBalance(balance).variantId).toBe(balance.variantId)
  for (const change of [{ reserved: 11 }, { available: 10 }, { onHand: -1 }, { onHand: 2147483648 }, { reserved: 1.5 }, { variantId: 123 }]) expect(() => decodeBalance({ ...balance, ...change })).toThrow()
})
test('movement validates signed deltas, UTC and known kinds', () => {
  expect(decodeMovement({ ...movement, onHandDelta: -2 }).onHandDelta).toBe(-2)
  for (const change of [{ onHandDelta: -2147483649 }, { kind: 'Fake' }, { createdAt: 'yesterday' }, { createdAt: '2026-02-31T00:00:00Z' }, { actorUserId: 'x' }, { operationKey: 'x' }]) expect(() => decodeMovement({ ...movement, ...change })).toThrow()
  expect(() => decodeStockResult({ inventory: balance, movement: { ...movement, variantId: '2' }, isReplay: true })).toThrow()
})
test('restored command keeps key and payload across reload and is isolated by actor/variant', () => {
  const actor = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', other = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
  const command = { variantId: balance.variantId, kind: 'receive' as const, delta: 10, reason: 'Synthetic', operationKey: movement.operationKey }
  storePending(actor, command)
  expect(readPending(actor, balance.variantId)).toEqual(command)
  expect(readPending(other, balance.variantId)).toBeUndefined()
  expect(readPending(actor, '2')).toBeUndefined()
})
test('unsafe commands cannot be persisted or sent', () => {
  const command = { variantId: '1', kind: 'adjust', delta: -2, reason: 'Synthetic', operationKey: movement.operationKey }
  expect(decodeCommand(command).delta).toBe(-2)
  for (const change of [{ delta: 0 }, { delta: 1.5 }, { delta: -2147483649 }, { reason: '' }, { reason: 'x'.repeat(301) }, { kind: 'receive', delta: -1 }, { operationKey: '00000000-0000-0000-0000-000000000000' }]) expect(() => decodeCommand({ ...command, ...change })).toThrow()
})
test('a valid-looking response for a different operation is rejected rather than showing success', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ token: 'synthetic-csrf' }))).mockResolvedValueOnce(new Response(JSON.stringify({ inventory: balance, movement, isReplay: false }), { status: 201 }))
  vi.stubGlobal('fetch', fetchMock)
  await expect(inventoryApi.write({ variantId: balance.variantId, kind: 'receive', delta: 10, reason: movement.reason, operationKey: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb' })).rejects.toMatchObject({ code: 'INVALID_RESPONSE' })
})
