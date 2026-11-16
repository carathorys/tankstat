/** The day an instant falls on in the browser's own time zone, as YYYY-MM-DD (the day a date and time shown here is on). */
export const dayIso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

/** Today as YYYY-MM-DD in the browser's own time zone (the day a person means by "today"). */
export const todayIso = () => dayIso(new Date())

/** The picker's language for the UI language: Hungarian, British English (day first) or US English. */
export function pickerLocale(language: string): 'hu' | 'en-gb' | 'en' {
  const lower = language.toLowerCase()
  if (lower.startsWith('hu')) return 'hu'
  return lower === 'en-gb' ? 'en-gb' : 'en'
}
