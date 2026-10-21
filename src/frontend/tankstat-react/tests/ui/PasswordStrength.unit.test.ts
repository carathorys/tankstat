import { describe, expect, it } from 'vitest'
import { passwordStrength } from '../../src/forms/passwordStrength.ts'

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

  it('finds a common password in every spelling: with the swaps the list holds, and accented', () => {
    expect(passwordStrength('mytrustno1pass').reasons).toContain('common')
    expect(passwordStrength('xxabc123xx').reasons).toContain('common')
    expect(passwordStrength('Jelszó2024!')).toEqual(passwordStrength('Jelszo2024!'))
    expect(passwordStrength('Jelszó2024!').reasons).toContain('common')
  })

  it('prices an accented letter as a letter, not a symbol', () => {
    expect(passwordStrength('árvíztűrőtükörfúrógép').score).toBe(passwordStrength('arvizturotukorfurogep').score)
    expect(passwordStrength('árvíztűrőtükör').reasons).toContain('oneKind')
  })

  it("never counts the address's top-level domain as the person's own", () => {
    const me = ['jane.doe@gmail.com', 'Jane Doe']

    expect(passwordStrength('Comfortable Mug 42', me)).toEqual(passwordStrength('Comfortable Mug 42'))
    expect(passwordStrength('janedoe-river-42', me).reasons).toContain('containsName')
    expect(passwordStrength('kovacsgabor1985', ['kg85@gmail.com', 'Kovács Gábor']).reasons).toContain('containsName') // accents fold too
  })

  it('marks the characters a word covers, also after a character outside the BMP', () => {
    // An emoji is one character but two UTF-16 units: the common word is still found where it is, and nothing after it is marked.
    expect(passwordStrength('🚗password#9%xQ')).toEqual(passwordStrength('&password#9%xQ'))
  })

  it('counts the minimum as the form and the server do', () => {
    // Six emoji are 12 UTF-16 units: long enough for a minimum of 10 there, so not "too short" here either.
    expect(passwordStrength('😀😀😀😀😀😀', [], 10).reasons).not.toContain('tooShort')
  })

  it('stays quick however much is pasted', () => {
    const pasted = Array.from({ length: 5000 }, (_, i) => 'abcdefghijklmnopqrstuvwxyz0123456789!@#'[(i * 7919) % 39]).join('')
    const start = performance.now()

    expect(passwordStrength(pasted).score).toBe(4)
    expect(performance.now() - start).toBeLessThan(1000)
  })
})
