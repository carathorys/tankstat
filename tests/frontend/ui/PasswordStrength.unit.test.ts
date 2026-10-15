import { describe, expect, it } from 'vitest'
import { passwordStrength } from '../../../src/frontend/forms/passwordStrength.ts'

describe('the strength of a new password', () => {
  it.each([
    ['password1', 0], // a common password and a digit
    ['p4ssw0rd2024', 0], // the same with the usual swaps
    ['qwertyuiop12', 0], // a keyboard row
    ['aaaaaaaaaaaa', 0], // one character again and again
    ['abcdefgh1234', 0], // two runs
    ['Tr0ub4dor&3', 1], // a word with swaps and a symbol: guessable
    ['k8#Qz!v2Lm@xR4t$', 3], // random, of every kind
    ['correct horse battery staple', 4], // a long phrase of four words
  ])('%s scores %i', (password, score) => {
    expect(passwordStrength(password).score).toBe(score)
  })

  it('pins the scores the handoff names', () => {
    expect(passwordStrength('password1').score).toBe(0)
    expect(passwordStrength('Tr0ub4dor&3').score).toBeLessThanOrEqual(2)
    expect(passwordStrength('correct horse battery staple').score).toBe(4)
  })

  it("costs less where it holds the person's own address or name", () => {
    const own = passwordStrength('alicesmith2024!', ['alice.smith@example.com', 'Alice Smith'])
    const other = passwordStrength('alicesmith2024!', ['bob@example.com', 'Bob'])

    expect(own.score).toBeLessThan(other.score)
    expect(own.reasons).toContain('containsName')
    expect(other.reasons).not.toContain('containsName')
  })

  it('says which patterns made it weak, in the order they matter', () => {
    expect(passwordStrength('password1').reasons).toEqual(['tooShort', 'common'])
    expect(passwordStrength('aaaaaaaaaaaa').reasons).toEqual(['repeats', 'oneKind'])
    expect(passwordStrength('abcdefgh1234').reasons).toEqual(['sequence', 'keyboardRow'])
    expect(passwordStrength('qwertyuiop12').reasons).toEqual(['common', 'keyboardRow'])
  })

  it('is very weak under the minimum, whatever it holds, and tells nothing to a strong one', () => {
    expect(passwordStrength('k8#Qz!v2Lm@xR4t$', [], 20)).toEqual({ score: 0, reasons: ['tooShort'] })
    expect(passwordStrength('correct horse battery staple').reasons).toEqual([])
    expect(passwordStrength('')).toEqual({ score: 0, reasons: [] })
  })
})
