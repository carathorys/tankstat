import { Languages } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { DateFormat, TimeFormat } from '../gql/generated.ts'
import { sampleDate, sampleTime } from '../i18n/format.ts'
import { DATE_FORMATS, setFormats, TIME_FORMATS, useFormats } from '../i18n/formatStore.ts'
import { LANGUAGES } from '../i18n/index.ts'
import { validLocale } from '../i18n/locale.ts'
import { useUiSettings } from '../settings/uiSettingsContext.ts'
import { RadioMenu } from './RadioMenu.tsx'

/** The menu's value for "no format chosen": the instance's default, else the language's own. */
const DEFAULT = 'default'

/**
 * The language and how times and dates are written (each as the language writes it unless chosen; an instance may set its own default,
 * which "Default" then follows). Available on every screen (also before sign-in); remembered in the browser and, once signed in, with the
 * account. Every format is offered with an example, so its name needs no explaining.
 */
export function LanguageMenu() {
  const { t, i18n } = useTranslation()
  const settings = useUiSettings()
  const formats = useFormats()
  const language = i18n.resolvedLanguage ?? 'en'
  // The examples in the tag every page writes with (en-GB stays en-GB, though its texts are the English ones), so each shows what choosing it gives.
  const writing = validLocale(i18n.language, i18n.resolvedLanguage)
  const option = (value: string, name: string, example: string) => ({ value, label: t('formats.option', { name, example }) })
  const chooseTime = (format: TimeFormat | null) => (settings ? settings.setTimeFormat(format) : setFormats({ time: format }))
  const chooseDate = (format: DateFormat | null) => (settings ? settings.setDateFormat(format) : setFormats({ date: format }))
  return (
    <RadioMenu
      label={t('language.menu')}
      icon={<Languages size={18} aria-hidden />}
      groups={[
        {
          label: t('language.label'),
          value: language,
          options: LANGUAGES.map((l) => ({ value: l.code, label: l.name, lang: l.code })),
          onChange: (code) => {
            void i18n.changeLanguage(code)
            settings?.setLanguage(code)
          },
        },
        {
          label: t('formats.time'),
          value: formats.time ?? DEFAULT,
          options: [
            option(DEFAULT, t('formats.default'), sampleTime(writing, formats.defaultTime)),
            ...TIME_FORMATS.map((f) => option(f, t(`formats.times.${f}`), sampleTime(writing, f))),
          ],
          onChange: (value) => chooseTime(value === DEFAULT ? null : (value as TimeFormat)),
        },
        {
          label: t('formats.date'),
          value: formats.date ?? DEFAULT,
          options: [
            option(DEFAULT, t('formats.default'), sampleDate(writing, formats.defaultDate)),
            ...DATE_FORMATS.map((f) => ({ value: f, label: sampleDate(writing, f) })),
          ],
          onChange: (value) => chooseDate(value === DEFAULT ? null : (value as DateFormat)),
        },
      ]}
    />
  )
}
