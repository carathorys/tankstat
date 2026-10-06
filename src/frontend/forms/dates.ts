/** Today as YYYY-MM-DD in the browser's own time zone (the day a person means by "today"). */
export const todayIso = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/** The picker's language for the UI language: Hungarian, British English (day first) or US English. */
export function pickerLocale(language: string): 'hu' | 'en-gb' | 'en' {
  const lower = language.toLowerCase()
  if (lower.startsWith('hu')) return 'hu'
  return lower === 'en-gb' ? 'en-gb' : 'en'
}
