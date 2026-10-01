import i18n from 'i18next'
import LanguageDetector from 'i18next-browser-languagedetector'
import resourcesToBackend from 'i18next-resources-to-backend'
import { initReactI18next } from 'react-i18next'
import en from './locales/en.json'

/** Languages the UI is translated into; names are written in the language itself and never translated. */
export const LANGUAGES = [
  { code: 'en', name: 'English' },
  { code: 'hu', name: 'Magyar' },
] as const

export type LanguageCode = (typeof LANGUAGES)[number]['code']

const STORAGE_KEY = 'tankstat.language'

/**
 * English is bundled (it is also the fallback); other languages are loaded on demand when selected.
 * Pass `language` to skip detection (tests); otherwise the saved choice, then the browser language, is used.
 */
export async function initI18n(language?: LanguageCode) {
  const instance = i18n.use(initReactI18next).use(resourcesToBackend((lng: string) => import(`./locales/${lng}.json`)))
  if (!language) instance.use(LanguageDetector)

  await instance.init({
    lng: language,
    fallbackLng: 'en',
    supportedLngs: LANGUAGES.map((l) => l.code),
    nonExplicitSupportedLngs: true, // hu-HU -> hu
    resources: { en: { translation: en } },
    partialBundledLanguages: true,
    interpolation: { escapeValue: false }, // React escapes
    detection: { order: ['localStorage', 'navigator'], lookupLocalStorage: STORAGE_KEY, caches: ['localStorage'] },
    react: { useSuspense: false },
  })

  const sync = (lng: string) => {
    document.documentElement.lang = lng
  }
  sync(i18n.language)
  i18n.on('languageChanged', sync)
  return i18n
}

export { i18n }
