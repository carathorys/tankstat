import { useTranslation } from 'react-i18next'
import type { BackupImportReviewQuery, ImportKindMode } from '../../gql/generated.ts'

/** The kinds of a car's entries a choice is made for (#338), as the server names them. */
export type Kind = 'refueling' | 'expense' | 'recurring'
export const KINDS: readonly Kind[] = ['refueling', 'expense', 'recurring']

/**
 * What comes of one kind. For *Choose…* the picks are a default and its exceptions (the backup's entry ids), never every id: `addNew`
 * for the entries only in the backup, `takeBackups` for the ones that differ.
 */
export interface KindChoice {
  mode: ImportKindMode
  addNew: boolean
  newExceptions: string[]
  takeBackups: boolean
  differExceptions: string[]
}
export type Kinds = Record<Kind, KindChoice>

/** The tyres chosen of a car (#339): every new set but the unticked ones, and whether the lines of no set come. */
export interface TyreChoice {
  except: string[]
  noSetLines: boolean
  /** How many tyre sets and line groups come by this choice, once the rows have been shown (null before): a car can bring only its tyres. */
  coming: number | null
}
export const defaultTyres = (): TyreChoice => ({ except: [], noSetLines: true, coming: null })

/** What else of a car comes (#339): its log photos, its picture, its charts, its dashboard. A null is "as the car is": taken when the car has none. */
export interface MediaChoice {
  photos: boolean
  picture: boolean | null
  charts: boolean
  dashboard: boolean | null
}
export const defaultMedia = (): MediaChoice => ({ photos: true, picture: null, charts: true, dashboard: null })

/** The media as the server takes them: a picture or dashboard left as the car is (null) comes when the car has none, which the server decides. */
export const mediaInput = (media: MediaChoice) => ({ photos: media.photos, picture: media.picture, charts: media.charts, dashboard: media.dashboard })

/** The tyre choice as the server takes it. */
export const tyresInput = (tyres: TyreChoice) => ({ except: tyres.except, noSetLines: tyres.noSetLines })

export type CarReview = BackupImportReviewQuery['backupImportReview'][number]
export type SetOffer = CarReview['tyres']['sets'][number]
export type KindReview = CarReview['kinds'][number]

const missing = (): KindChoice => ({ mode: 'ONLY_MISSING', addNew: true, newExceptions: [], takeBackups: false, differExceptions: [] })
export const defaultKinds = (): Kinds => ({ refueling: missing(), expense: missing(), recurring: missing() })

/** How many of a section's `total` entries a default with its exceptions picks. */
const picks = (byDefault: boolean, exceptions: readonly string[], total: number) => Math.max(0, Math.min(total, byDefault ? total - exceptions.length : exceptions.length))
export const addedOf = (choice: KindChoice, review: Pick<KindReview, 'new'>) => picks(choice.addNew, choice.newExceptions, review.new)
export const takenOf = (choice: KindChoice, review: Pick<KindReview, 'differ'>) => picks(choice.takeBackups, choice.differExceptions, review.differ)

export const replaces = (kinds: Kinds) => KINDS.some((kind) => kinds[kind].mode === 'REPLACE_ALL')
/** Every log kind left out and no tyres coming (#339: tyres alone are an import): the server refuses the same. */
export const nothingChosen = (kinds: Kinds, tyres?: TyreChoice) => KINDS.every((kind) => kinds[kind].mode === 'DONT_IMPORT') && !((tyres?.coming ?? 0) > 0)

/** The kinds as the server takes them: one left at *Only the missing* is left out. A new car's kind comes whole, or not at all. */
export function kindsInput(kinds: Kinds, asNew: boolean) {
  const of = (kind: Kind) => {
    const choice = kinds[kind]
    if (choice.mode === 'DONT_IMPORT') return { mode: choice.mode }
    if (asNew || choice.mode === 'ONLY_MISSING') return undefined
    if (choice.mode === 'REPLACE_ALL') return { mode: choice.mode }
    return { mode: choice.mode, addNew: choice.addNew, newExceptions: choice.newExceptions, takeBackups: choice.takeBackups, differExceptions: choice.differExceptions }
  }
  return { refuelings: of('refueling'), expenses: of('expense'), schedules: of('recurring') }
}

/** Why *Replace all* cannot be chosen for a kind (the owner's decisions 2 and 4), in words; null when it can. */
export function useHeldBack() {
  const { t } = useTranslation()
  return (kind: Kind, review: KindReview): string | null => {
    // Photos hold nothing back since #339 (they come back with their entries; the confirmation counts the ones that do not).
    if (review.onTyreLines > 0) return t('import.kinds.expense.heldLines', { lines: review.onTyreLines })
    if (review.backup === 0) return t(`import.kinds.${kind}.noneInBackup`)
    return null
  }
}

/** What a kind's choice does, in one line (the Cars step and the confirmation use the same words). */
export function useKindLine() {
  const { t } = useTranslation()
  return (kind: Kind, choice: KindChoice, review: KindReview, car: string): string => {
    if (choice.mode === 'DONT_IMPORT') return t(`import.kinds.${kind}.none`)
    if (choice.mode === 'REPLACE_ALL')
      return review.targetTrashed > 0
        ? t(`import.kinds.${kind}.replace`, { car, count: review.targetLive, trash: review.targetTrashed, total: review.backup })
        : t(`import.kinds.${kind}.replaceNoTrash`, { car, count: review.targetLive, total: review.backup })
    if (choice.mode === 'CHOOSE') return t('import.kinds.chosen', { picked: addedOf(choice, review), new: review.new, taken: takenOf(choice, review), differ: review.differ })
    return t('import.kinds.missing', { new: review.new, differ: review.differ, same: review.same })
  }
}
