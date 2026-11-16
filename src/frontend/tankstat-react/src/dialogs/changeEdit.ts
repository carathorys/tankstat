import type { Change } from '../offline/changes.ts'

/**
 * Editing a change (on Waiting to sync: one kept on this device, or one the server parked) in the dialog of the entry it concerns: the
 * values the change carries, and the dialog's own title and Save label. The dialog loads nothing and offers no photos; the caller's
 * `onSubmit` decides what saving does (keep the change, or apply it on the server) and an error it throws stays in the dialog.
 */
export interface ChangeEdit<T> {
  initial: Partial<T>
  title: string
  submitLabel: string
  /** A waiting add whose amounts were left for its kept photos to fill: they may stay empty in the edit too. */
  mayWait?: boolean
  /** The log the change is about (its id), so a dialog can find what is tied to it (an expense's tyre log line, #290). */
  targetId?: string
  /** A change the server parked (Edit and apply), not one waiting here: its refusal says what it names may be gone (#290). */
  parked?: boolean
  /** Merging a parked edit with what is on the server now (`initial` is the merged values): what to say under each field. */
  merge?: MergeInfo
  /**
   * The changes waiting on this device that saving the dialog's values would remove (#330): `values` are those the dialog knows as they
   * stand (the amount, the tyre details), over the change's own. The dialog says so in a note before Save; Save asks first.
   */
  leftBehind?: (values: Record<string, unknown>) => Change[]
}

/** Thrown by an edit's save when the person chose Cancel in its confirmation: the dialog stays as it was, with no error. */
export class SaveCancelled extends Error {
  constructor() {
    super('The save was cancelled.')
  }
}

/**
 * After Cancel in an edit's confirmation: the focus back on the form's Save, once it can take it again (it was off while the question was
 * open).
 */
export const focusSaveAgain = (form: HTMLFormElement) => setTimeout(() => form.querySelector<HTMLButtonElement>('button[type="submit"]')?.focus())

/** One value of a field changed on both sides: as the change's input has it, and in words. */
export interface MergeSide {
  value: unknown
  text: string
}

export interface MergeInfo {
  /** Per field changed on both sides: this device's value (where it starts) and the server's, each one offered while it is not there. */
  conflicts: Record<string, { mine: MergeSide; theirs: MergeSide }>
  /** Per field changed on one side only, which side its value came from. */
  merged: Record<string, 'mine' | 'theirs'>
}
