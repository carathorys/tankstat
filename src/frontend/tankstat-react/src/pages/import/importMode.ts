import { createContext, useContext, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import type { ImportRunFieldsFragment } from '../../gql/generated.ts'

/**
 * Whether the import's pieces (the review card, *Choose what to import*, the confirmation, the run and its report) serve *Restore
 * cars* (#340): then they ask the restore's queries and say a restore's words where an import's would be wrong.
 */
export const RestoreMode = createContext(false)

export const useRestoreMode = () => useContext(RestoreMode)

/**
 * The import's texts a restore words otherwise, by their key without a plural ending: `import.<path>` is read as `restore.<path>`.
 * Everything else is the import's own (the owner's answer on #340: the restore's words for *Choose what to …*, #337's run texts worded
 * "restore").
 */
const RESTORE_WORDS = new Set([
  'import.kinds.open',
  'import.kinds.closedHint',
  'import.kinds.modes.DONT_IMPORT',
  'import.kinds.confirm.title',
  'import.kinds.confirm.titleOf',
  'import.kinds.confirm.text',
  'import.kinds.confirm.undone',
  'import.kinds.confirm.confirm',
  'import.plan.policyRest',
  'import.plan.inTrash',
  'import.run.running',
  'import.run.cancel',
  'import.run.cancelTitle',
  'import.run.cancelText',
  'import.run.keep',
  'import.run.waiting',
  'import.run.finished',
  'import.run.imported',
  'import.run.nothing',
  'import.run.another',
  'import.run.gone',
  'import.media.charts',
  'import.media.importedPhotos',
  'import.media.importedPicture',
  'import.media.importedCharts',
  'import.media.importedDashboard',
  'import.media.importedOwnDashboard',
])

/** The restore's key for one of the import's, or the key itself. */
export function restoreKey(key: string): string {
  if (RESTORE_WORDS.has(key) || key.startsWith('import.run.failure.')) return key.replace(/^import\./, 'restore.')
  return key
}

/** `useTranslation` whose `t` says a restore's words in the restore's mode. */
export function useImportTranslation() {
  const result = useTranslation()
  const restore = useRestoreMode()
  const { t } = result
  const swapped = useMemo(() => {
    if (!restore) return t
    const plain = t as unknown as (key: string, ...rest: unknown[]) => string
    return ((key: string, ...rest: unknown[]) => plain(restoreKey(key), ...rest)) as unknown as typeof t
  }, [restore, t])
  return { ...result, t: swapped }
}

/** A run that goes or waits. */
export const active = (run: ImportRunFieldsFragment | undefined) => run?.state === 'WAITING' || run?.state === 'RUNNING'
