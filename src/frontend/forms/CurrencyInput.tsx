import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { currencyCodes, currencyMatches, currencyName } from './currencies.ts'
import { FieldAutocomplete } from './FieldAutocomplete.tsx'

/**
 * The currency of a Field: the three-letter code, typed or picked from the codes the browser knows (shown with their names in the UI
 * language, found by either); the vehicle's usual one first.
 */
export function CurrencyInput({ value, onChange, preferred }: { value: string; onChange: (value: string) => void; preferred?: string | null }) {
  const { i18n } = useTranslation()
  const language = i18n.language
  const codes = useMemo(() => currencyCodes(preferred), [preferred])
  const names = useMemo(() => new Map(codes.map((code) => [code, currencyName(code, language)])), [codes, language])
  return (
    <FieldAutocomplete
      options={codes}
      value={value}
      onChange={onChange}
      uppercase
      optionLabel={(code) => (names.get(code) === code ? code : `${code} – ${names.get(code)}`)}
      filter={(code, typed) => currencyMatches(code, names.get(code) ?? code, typed)}
    />
  )
}
