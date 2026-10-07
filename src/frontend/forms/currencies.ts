/** A few common ones, for a browser without Intl.supportedValuesOf. */
const FALLBACK = ['EUR', 'USD', 'GBP', 'HUF', 'CHF', 'CZK', 'PLN', 'RON', 'SEK', 'NOK', 'DKK', 'JPY', 'CAD', 'AUD']

/** Every ISO 4217 currency code the browser knows, `preferred` (the vehicle's usual one) first. */
export function currencyCodes(preferred?: string | null): string[] {
  let all: string[]
  try {
    all = Intl.supportedValuesOf('currency')
  } catch {
    all = FALLBACK
  }
  const first = preferred?.trim().toUpperCase()
  return first && all.includes(first) ? [first, ...all.filter((c) => c !== first)] : [...all]
}

/** A currency's name in the UI language ("Hungarian forint", "magyar forint"), or the code when the browser has none. */
export function currencyName(code: string, language: string): string {
  try {
    return new Intl.DisplayNames([language], { type: 'currency' }).of(code) ?? code
  } catch {
    return code
  }
}

/** A currency suits what was typed when its code starts with it or its name contains it. */
export const currencyMatches = (code: string, name: string, typed: string) =>
  code.startsWith(typed.toUpperCase()) || name.toLowerCase().includes(typed.toLowerCase())
