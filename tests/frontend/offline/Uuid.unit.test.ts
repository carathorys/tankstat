import { describe, expect, it } from 'vitest'
import { uuidV4 } from '../../../src/frontend/offline/uuid.ts'
import { UUID } from '../support/ids.ts'

describe('uuidV4', () => {
  it('is a version 4 UUID with the RFC 4122 variant, from random bytes', () => {
    expect(uuidV4()).toMatch(UUID)
    expect(uuidV4()).not.toBe(uuidV4())
  })

  it('sets only the version and variant bits of the random bytes', () => {
    expect(uuidV4((n) => new Uint8Array(n).fill(0xff))).toBe('ffffffff-ffff-4fff-bfff-ffffffffffff')
    expect(uuidV4((n) => new Uint8Array(n))).toBe('00000000-0000-4000-8000-000000000000')
  })
})
