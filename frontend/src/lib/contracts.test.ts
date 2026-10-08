import { expect, test } from 'vitest'
import { formatVnd, formatVietnamTime, pagedDecoder, parseEntityId, parseMoney } from './contracts'
import { idPageFixture } from '../test/fixtures'
import { isRecord } from '../services/errors'

test('SQL bigint beyond JS safe integer survives decode as string', () => {
  const decode = pagedDecoder(value => {
    if (!isRecord(value)) throw new TypeError('Invalid item')
    return { id: parseEntityId(value.id) }
  })
  expect(decode(JSON.parse(JSON.stringify(idPageFixture))).items[0].id).toBe('9223372036854775807')
})

test.each([1, '0', '01', '-1', '9223372036854775808'])('invalid bigint %s is rejected', value => {
  expect(() => parseEntityId(value)).toThrow()
})

test('VND rejects fractions, unsafe or negative money and uses Vietnamese formatting', () => {
  expect(formatVnd(1000000).replace(/\s/g, '')).toBe('1.000.000₫')
  for (const value of [-1, 0.5, 9007199254740992, '1000']) expect(() => parseMoney(value)).toThrow()
})

test('UTC date formats correctly across the Vietnamese midnight boundary', () => {
  expect(formatVietnamTime('2026-10-06T17:00:00Z')).toContain('07/10/2026')
  expect(formatVietnamTime('2026-10-06T17:00:00Z')).toContain('00:00')
  expect(() => formatVietnamTime('2026-10-06T17:00:00')).toThrow()
})

test.each([{ ...idPageFixture, page: 0 }, { ...idPageFixture, pageSize: 101 }, { ...idPageFixture, totalCount: -1 }, { ...idPageFixture, items: null }])('invalid pagination contract is rejected', value => {
  expect(() => pagedDecoder(parseEntityId)(value)).toThrow()
})

test('invalid calendar timestamps cannot silently roll into another Vietnamese date', () => {
  expect(() => formatVietnamTime('2026-02-30T17:00:00Z')).toThrow()
})
