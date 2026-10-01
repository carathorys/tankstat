import 'i18next'
import type en from './i18n/locales/en.json'

// Makes t('...') keys and their interpolation values type-checked against the English catalog.
declare module 'i18next' {
  interface CustomTypeOptions {
    defaultNS: 'translation'
    resources: { translation: typeof en }
  }
}
