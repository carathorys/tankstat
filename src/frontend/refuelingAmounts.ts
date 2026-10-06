import { parseDecimal } from './i18n/format.ts'
import { calculateField, clearCalculated, type FillState } from './recognition/readValues.ts'

/** The amounts of a refuelling that follow each other: total = volume × unit price. */
export type AmountField = 'volume' | 'unitPrice' | 'totalCost'

/**
 * Among the amounts nobody typed, the one that gives way first: the unit price is never saved, and the volume, which consumption is
 * worked out from, last.
 */
const GIVES_WAY: readonly AmountField[] = ['unitPrice', 'totalCost', 'volume']

/** What each amount is worked out from: the total is the product of the other two, the volume and the unit price are quotients. */
const SOURCES: Record<AmountField, readonly [AmountField, AmountField]> = {
  totalCost: ['volume', 'unitPrice'],
  volume: ['totalCost', 'unitPrice'],
  unitPrice: ['totalCost', 'volume'],
}

/** Decimals of a calculated amount: money to the cent, volume and unit price to the thousandth (as the server keeps them). */
const DECIMALS: Record<AmountField, number> = { totalCost: 2, volume: 3, unitPrice: 3 }

/**
 * Keeps volume, unit price and total in step once `changed` moved: the least recently typed of the other amounts is worked out from the
 * two it depends on. Amounts nobody typed (empty, loaded, read from a photo, calculated) come first, in `GIVES_WAY` order, then the ones
 * typed longest ago. A change that is not the user's (`byUser` false: a photo, the starting values) never moves what the user typed.
 * When an amount cannot be worked out (what it depends on is empty or not a number), the next one is tried; a calculated one is emptied
 * on the way, so it never shows a value that no longer follows. What the user typed is never emptied.
 */
export function keepAmountsInStep<F extends string>(
  state: FillState<F | AmountField>,
  changed: readonly (F | AmountField)[],
  byUser: boolean,
): FillState<F | AmountField> {
  const moved = GIVES_WAY.filter((field) => changed.includes(field))
  if (moved.length === 0 || moved.length === GIVES_WAY.length) return state

  // The amounts the user typed (and did not empty since), from the least to the most recently typed; -1 for the others.
  const typed = [...state.touched].filter((field) => state.values[field].trim() !== '')
  const age = (field: AmountField) => typed.indexOf(field)
  const candidates = GIVES_WAY.filter((field) => !moved.includes(field) && (byUser || age(field) < 0)).sort((a, b) => age(a) - age(b))

  let next = state
  for (const field of candidates) {
    const value = workedOut(next.values, field)
    if (value !== undefined) return calculateField(next, field, value)
    next = clearCalculated(next, field)
  }
  return next
}

/** The amount worked out from the two it depends on, as text, or undefined when they are not both positive numbers. */
function workedOut(values: Record<AmountField, string>, field: AmountField): string | undefined {
  const [a, b] = SOURCES[field]
  const x = positive(values[a])
  const y = positive(values[b])
  if (x === undefined || y === undefined) return undefined
  const value = rounded(field === 'totalCost' ? x * y : x / y, DECIMALS[field])
  if (value === undefined) return undefined
  // Written the way the user writes numbers: with a decimal comma when what it came from has one.
  return values[a].includes(',') || values[b].includes(',') ? String(value).replace('.', ',') : String(value)
}

function positive(text: string): number | undefined {
  const value = parseDecimal(text)
  return value !== undefined && value > 0 ? value : undefined
}

/**
 * Rounded half up to `decimals` (through the exponent, so 1.005 becomes 1.01, not 1.00), or undefined outside the amounts a refuelling
 * can have (a tiny value would round to 0, and a huge one is written with an exponent).
 */
function rounded(value: number, decimals: number): number | undefined {
  if (!(value > 0 && value < 1e9)) return undefined
  const result = Number(`${Math.round(Number(`${value}e${decimals}`))}e-${decimals}`)
  return result > 0 ? result : undefined
}
