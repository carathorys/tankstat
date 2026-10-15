/**
 * How hard a new password would be to guess, 0 (very weak) to 4 (strong): an estimate of our own, advisory only (it never stops a form;
 * the server judges the minimum). Every character costs what guessing it would: a letter in a word is cheap, a digit or a symbol dearer,
 * and what follows a pattern almost free (the same character again, a run like `abc` or `321`, a keyboard row, a common password, the
 * person's own name or address). The reasons say which patterns cost the most, as keys of `forms.password.strength.reasons`.
 */
export type StrengthReason = 'tooShort' | 'common' | 'containsName' | 'repeats' | 'sequence' | 'keyboardRow' | 'oneKind'
export type Score = 0 | 1 | 2 | 3 | 4
export type Strength = { score: Score; reasons: StrengthReason[] }

/** Bits needed for each score: below the first is 0, from the last on 4. */
const THRESHOLDS = [26, 36, 48, 60] as const

/** What a pattern's character still costs once the pattern is seen. */
const PATTERN_BITS = 0.5
/** What a whole common password or a piece of the person's own name costs: one guess among a short list. */
const LISTED_BITS = 6

const KEYBOARD_ROWS = ['1234567890', 'qwertyuiop', 'asdfghjkl', 'zxcvbnm', 'qwertzuiop', 'yxcvbnm', 'azertyuiop', 'qsdfghjklm', 'wxcvbn']

/** The passwords tried first (in English and Hungarian), compared after undoing the usual swaps (`p4ssw0rd`). */
const COMMON = [
  'password', 'passwort', 'jelszo', 'jelszavam', 'qwerty', 'qwertz', 'letmein', 'welcome', 'admin', 'administrator', 'iloveyou', 'monkey',
  'dragon', 'sunshine', 'princess', 'football', 'baseball', 'master', 'shadow', 'superman', 'trustno1', 'secret', 'titkos', 'tankstat',
  'abc123', 'login', 'changeme', 'default', 'hello', 'freedom', 'whatever', 'starwars', 'pokemon', 'szerelem', 'macska', 'kutya',
]

const LEET: Record<string, string> = { '0': 'o', '1': 'i', '3': 'e', '4': 'a', '5': 's', '7': 't', '@': 'a', $: 's', '!': 'i' }

/**
 * The most of a password that is looked at: what the server takes at most (it refuses a longer password anyway), and plenty to advise on a
 * passphrase. It keeps every keystroke cheap however much is pasted: the repeated pieces cost the cube of the length.
 */
const MAX_ANALYSED = 128

/**
 * A character as the patterns and the lists see it: lowercase and without its accents (`Ó` is `o`, so `jelszó` is the listed `jelszo`), the
 * final sigma as any other; one per character, so a position in the password stays a position in the result.
 */
function folded(char: string): string {
  const lower = char.toLowerCase()
  return (lower.normalize('NFD').replace(/\p{M}/gu, '') || lower).replace('ς', 'σ')
}

/** The common passwords as the password is compared with them: folded, the usual swaps undone like the password's (`trustno1`). */
const COMMON_FOLDED = COMMON.map((word) => Array.from(word, (c) => LEET[c] ?? folded(c)))

/** What a character costs on its own, by kind: letters of words are predictable (accented ones too), the first capital nearly free. */
function charBits(char: string, index: number): number {
  if (/\p{Ll}/u.test(char)) return 2.5
  if (/\p{Lu}/u.test(char)) return index === 0 ? 1 : 3
  if (/\p{Nd}/u.test(char)) return 3.3
  if (char === ' ') return 1
  return 4
}

/**
 * The person's own words worth looking for, folded: the parts of a name, and of an address all but its top-level domain (`com` is
 * everybody's), with three characters or more.
 */
function personalWords(personal: readonly (string | null | undefined)[]): string[][] {
  return personal
    .flatMap((p) => {
      const text = (p ?? '').normalize('NFC')
      const at = text.lastIndexOf('@')
      return (at < 0 ? text : `${text.slice(0, at)} ${text.slice(at + 1).replace(/\.[^.]*$/, '')}`).split(/[^\p{L}\p{M}\p{N}]+/u)
    })
    .map((word) => Array.from(word, folded))
    .filter((word) => word.length >= 3)
}

export function passwordStrength(password: string, personal: readonly (string | null | undefined)[] = [], minLength = 10): Strength {
  if (password.length === 0) return { score: 0, reasons: [] }
  const text = password.normalize('NFC') // an accent typed as a mark of its own is one character with its letter
  const chars = Array.from(text).slice(0, MAX_ANALYSED)
  const lower = chars.map(folded)
  // Per character: what it costs (null until a pattern decides), and the pattern that made it cheap.
  const cost: (number | null)[] = chars.map(() => null)
  const found = new Set<StrengthReason>()
  let listed = 0

  // A word from a list, anywhere, compared character by character: its characters are free, the word costs one pick from the list.
  const markListed = (words: readonly (readonly string[])[], seen: readonly string[], reason: StrengthReason) => {
    for (const word of words)
      for (let at = 0; at + word.length <= seen.length; at++) {
        if (!word.every((c, k) => seen[at + k] === c)) continue
        if (cost.slice(at, at + word.length).some((c) => c === null)) {
          for (let i = at; i < at + word.length; i++) cost[i] = 0
          listed++
          found.add(reason)
        }
      }
  }
  markListed(personalWords(personal), lower, 'containsName')
  markListed(COMMON_FOLDED, lower.map((c) => LEET[c] ?? c), 'common')

  // Runs: the same character again, a step of one up or down (abc, 987), a stretch of a keyboard row; the first of a run costs as usual.
  for (let i = 2; i < chars.length; i++) {
    const [a, b, c] = [lower[i - 2], lower[i - 1], lower[i]]
    if (a === b && b === c) {
      cost[i] ??= PATTERN_BITS
      cost[i - 1] ??= PATTERN_BITS
      found.add('repeats')
    }
    const [x, y, z] = [a.codePointAt(0)!, b.codePointAt(0)!, c.codePointAt(0)!]
    if ((y - x === 1 && z - y === 1) || (x - y === 1 && y - z === 1)) {
      cost[i] ??= PATTERN_BITS
      cost[i - 1] ??= PATTERN_BITS
      found.add('sequence')
    }
  }
  for (let i = 3; i < chars.length; i++) {
    const piece = lower.slice(i - 3, i + 1).join('')
    const reversed = Array.from(piece).reverse().join('')
    if (KEYBOARD_ROWS.some((row) => row.includes(piece) || row.includes(reversed))) {
      for (let j = i - 2; j <= i; j++) cost[j] ??= PATTERN_BITS
      found.add('keyboardRow')
    }
  }
  // A piece repeated right after itself (abcabc, 1212): the second copy is nearly free.
  for (let size = 2; size * 2 <= chars.length; size++)
    for (let i = size * 2; i <= chars.length; i++)
      if (lower.slice(i - size * 2, i - size).join('') === lower.slice(i - size, i).join('')) {
        for (let j = i - size; j < i; j++) cost[j] ??= PATTERN_BITS
        found.add('repeats')
      }

  const bits = chars.reduce((sum, char, i) => sum + (cost[i] ?? charBits(char, i)), 0) + listed * LISTED_BITS
  let score = THRESHOLDS.filter((t) => bits >= t).length as Score
  // Counted as the field and the server count it (UTF-16 units), so the meter never asks for more than the form does.
  if (password.length < minLength) {
    score = 0
    found.add('tooShort')
  }
  const kinds = [/\p{Ll}/u, /\p{Lu}/u, /\p{N}/u, /[^\p{Ll}\p{Lu}\p{N}]/u].filter((kind) => kind.test(text)).length
  if (kinds === 1 && score < 3) found.add('oneKind')
  // The patterns in the order they matter; a password that is strong anyway is not told what to avoid.
  const order: StrengthReason[] = ['tooShort', 'common', 'containsName', 'repeats', 'sequence', 'keyboardRow', 'oneKind']
  return { score, reasons: score >= 3 ? [] : order.filter((r) => found.has(r)) }
}
