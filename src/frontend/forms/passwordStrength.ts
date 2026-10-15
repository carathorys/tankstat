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

/** What a character costs on its own, by kind: letters of words are predictable, the first capital nearly free. */
function charBits(char: string, index: number): number {
  if (/[a-z]/.test(char)) return 2.5
  if (/[A-Z]/.test(char)) return index === 0 ? 1 : 3
  if (/[0-9]/.test(char)) return 3.3
  if (char === ' ') return 1
  return 4
}

/** The person's own words worth looking for: the parts of an address or a name with three letters or more. */
function personalWords(personal: readonly (string | null | undefined)[]): string[] {
  return personal
    .flatMap((p) => (p ?? '').toLowerCase().split(/[^\p{L}\p{N}]+/u))
    .filter((w) => w.length >= 3)
}

export function passwordStrength(password: string, personal: readonly (string | null | undefined)[] = [], minLength = 10): Strength {
  if (password.length === 0) return { score: 0, reasons: [] }
  const chars = Array.from(password)
  const lower = chars.map((c) => c.toLowerCase())
  // Per character: what it costs (null until a pattern decides), and the pattern that made it cheap.
  const cost: (number | null)[] = chars.map(() => null)
  const found = new Set<StrengthReason>()
  let listed = 0

  // A word from a list, anywhere: its characters are free, the word costs one pick from the list.
  const markListed = (words: string[], text: string[], reason: StrengthReason) => {
    const joined = text.join('')
    for (const word of words) {
      let at = joined.indexOf(word)
      while (at >= 0) {
        if (cost.slice(at, at + word.length).some((c) => c === null)) {
          for (let i = at; i < at + word.length; i++) cost[i] = 0
          listed++
          found.add(reason)
        }
        at = joined.indexOf(word, at + 1)
      }
    }
  }
  markListed(personalWords(personal), lower, 'containsName')
  markListed(COMMON, lower.map((c) => LEET[c] ?? c), 'common')

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
  if (chars.length < minLength) {
    score = 0
    found.add('tooShort')
  }
  const kinds = [/[a-z]/, /[A-Z]/, /[0-9]/, /[^a-zA-Z0-9]/].filter((kind) => kind.test(password)).length
  if (kinds === 1 && score < 3) found.add('oneKind')
  // The patterns in the order they matter; a password that is strong anyway is not told what to avoid.
  const order: StrengthReason[] = ['tooShort', 'common', 'containsName', 'repeats', 'sequence', 'keyboardRow', 'oneKind']
  return { score, reasons: score >= 3 ? [] : order.filter((r) => found.has(r)) }
}
