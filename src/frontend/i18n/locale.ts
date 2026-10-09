/**
 * A language tag the platform accepts (`Intl`, `<html lang>`), made from whatever a browser reports: a POSIX locale such as
 * `en_GB.UTF-8@euro` or `en-US@posix` is not a BCP 47 tag, and `Intl` throws a RangeError on it. The first of these that is a valid tag:
 * the tag itself; with `_` as `-` and without a POSIX suffix (`.UTF-8`, `@posix`); the same cut back one subtag at a time from the right.
 * Else `fallback`, also for the POSIX locales that name no language (`C`, `POSIX`). Pure: it touches no page.
 */
export function validLocale(tag: string | null | undefined, fallback = 'en'): string {
  const text = (tag ?? '').trim()
  const base = text.replace(/[.@].*$/, '')
  if (/^(c|posix)$/i.test(base)) return fallback
  const parts = base.replace(/_/g, '-').split('-').filter(Boolean)
  const candidates = [text, ...parts.map((_, i) => parts.slice(0, parts.length - i).join('-'))]
  for (const candidate of candidates) {
    if (!candidate) continue
    try {
      const [canonical] = Intl.getCanonicalLocales(candidate)
      if (canonical) return canonical
    } catch {
      // not a valid tag: try a shorter one
    }
  }
  return fallback
}
