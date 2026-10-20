import { describe, expect, it } from 'vitest'
import { waitForRestart } from '../../src/admin/restartFlow.ts'

describe('waitForRestart', () => {
  const wait = { interval: 10, downFor: 50, upFor: 200 }

  /** A clock that moves by `interval` on every sleep, so the phases end by the counts, not by real time. */
  function clock() {
    let t = 0
    return { now: () => t, sleep: (ms: number) => Promise.resolve(void (t += ms)) }
  }

  it('waits for the old host to go, then for the new one to answer', async () => {
    const answers = [true, true, false, false, false, true]
    const c = clock()
    const probed: boolean[] = []
    const probe = () => Promise.resolve(answers[probed.push(true) - 1] ?? true)

    expect(await waitForRestart(probe, wait, c.sleep, c.now)).toBe('back')
    expect(probed).toHaveLength(6)
  })

  it('gives up on the old host going after downFor, and still waits for an answer', async () => {
    const c = clock()
    let probes = 0
    const probe = () => Promise.resolve(++probes > 0)

    expect(await waitForRestart(probe, wait, c.sleep, c.now)).toBe('back')
    expect(probes).toBe(6) // five answers within downFor (50 / 10), then the first one of the next phase
  })

  it('says so when the server does not come back within upFor', async () => {
    const c = clock()
    const probe = () => Promise.resolve(false)

    expect(await waitForRestart(probe, wait, c.sleep, c.now)).toBe('timeout')
    expect(c.now()).toBe(10 + 200)
  })
})
