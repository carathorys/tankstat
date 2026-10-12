import { useTranslation } from 'react-i18next'
import { useFormat } from '../i18n/format.ts'
import { labelKey } from './settingsText.ts'
import type { Unused } from './settingsRelevance.ts'

/** A setting's label in the page's language, or its key when it has none (a setting added on the server first). */
export function useSettingLabel() {
  const { t, i18n } = useTranslation()
  return (key: string): string => (i18n.exists(labelKey(key)) ? String(t(labelKey(key) as never)) : key)
}

/** Why a setting is not used, in words: the deciding setting by its label, and the values it would be used with ("Standalone or Oidc"). */
export function useUnusedText() {
  const { t } = useTranslation()
  const { either } = useFormat()
  const label = useSettingLabel()
  return (reason: Unused): string => {
    if (reason.kind === 'empty') return t('systemSettings.unused.empty', { setting: label(reason.setting) })
    const usedWith = either([...reason.usedWith])
    return reason.value === ''
      ? t('systemSettings.unused.notSet', { setting: label(reason.setting), usedWith })
      : t('systemSettings.unused.value', { setting: label(reason.setting), value: reason.value, usedWith })
  }
}
