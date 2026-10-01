import { useTranslation } from 'react-i18next'

/** Dates and numbers follow the selected UI language (Intl), never a hard-coded format. */
export function useFormat() {
  const { i18n } = useTranslation()
  const dateTime = new Intl.DateTimeFormat(i18n.language, { dateStyle: 'medium', timeStyle: 'short' })
  return { dateTime: (iso: string) => dateTime.format(new Date(iso)) }
}
