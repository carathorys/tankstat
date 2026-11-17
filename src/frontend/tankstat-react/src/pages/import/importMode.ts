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
  'import.kinds.allOrNothing',
  'import.kinds.closedHint',
  'import.kinds.confirm.confirm',
  'import.kinds.confirm.text',
  'import.kinds.confirm.title',
  'import.kinds.confirm.titleOf',
  'import.kinds.confirm.undone',
  'import.kinds.expense.heldLines',
  'import.kinds.expense.imported',
  'import.kinds.expense.leftRefused',
  'import.kinds.expense.none',
  'import.kinds.expense.replace',
  'import.kinds.expense.replaceNoTrash',
  'import.kinds.expense.replaced',
  'import.kinds.expense.reviewNone',
  'import.kinds.expense.reviewReplace',
  'import.kinds.finishedOf',
  'import.kinds.modes.DONT_IMPORT',
  'import.kinds.newCar',
  'import.kinds.noLinks',
  'import.kinds.open',
  'import.kinds.recurring.imported',
  'import.kinds.recurring.leftRefused',
  'import.kinds.recurring.none',
  'import.kinds.recurring.replace',
  'import.kinds.recurring.replaceNoTrash',
  'import.kinds.recurring.replaced',
  'import.kinds.recurring.reviewNone',
  'import.kinds.recurring.reviewReplace',
  'import.kinds.refueling.imported',
  'import.kinds.refueling.leftRefused',
  'import.kinds.refueling.none',
  'import.kinds.refueling.replace',
  'import.kinds.refueling.replaceNoTrash',
  'import.kinds.refueling.replaced',
  'import.kinds.refueling.reviewNone',
  'import.kinds.refueling.reviewReplace',
  'import.media.charts',
  'import.media.chartsRow',
  'import.media.dashboardKept',
  'import.media.dashboardReplaces',
  'import.media.importedCharts',
  'import.media.importedDashboard',
  'import.media.importedOwnDashboard',
  'import.media.importedPhotos',
  'import.media.importedPicture',
  'import.media.noPhotos',
  'import.media.ownCharts',
  'import.media.ownDashboard',
  'import.media.ownDashboardKept',
  'import.media.ownDashboardRow',
  'import.media.photos',
  'import.media.photosRow',
  'import.media.pictureKept',
  'import.media.pictureReplaces',
  'import.plan.inTrash',
  'import.plan.policyRest',
  'import.run.another',
  'import.run.cancel',
  'import.run.cancelText',
  'import.run.cancelTitle',
  'import.run.finished',
  'import.run.gone',
  'import.run.imported',
  'import.run.keep',
  'import.run.nothing',
  'import.run.running',
  'import.run.waiting',
  'import.tyres.amountsHere',
  'import.tyres.imported',
  'import.tyres.importedNoSet',
  'import.tyres.reminders',
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
