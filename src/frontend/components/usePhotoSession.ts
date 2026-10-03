import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { ReadingPurpose } from '../pictures/upload.ts'
import { usePhotoQueue, type PhotoQueue, type Saved } from './usePhotoQueue.ts'

export interface PhotoSession {
  queue: PhotoQueue
  /** Photos of a new log that the server could not attach: the dialog then says so instead of closing. */
  leftOut: number
  /** True from pressing Save until the log went through; the dialog must stay open and the photos untouched meanwhile. */
  saving: boolean
  /** Saves the log; a new one gets the uploaded drafts. Resolves to true when the dialog may close. */
  submit: (save: (photoIds: string[]) => Promise<Saved>, editing: boolean) => Promise<boolean>
  /** Forget everything (the dialog was closed); drafts that were not saved with a log are deleted. */
  reset: () => void
}

/**
 * The photo part of the add/edit dialogs of refuelings and expenses: the drafts of a new log and what became of them on save. With a
 * `purpose`, the server reads the photos too (when photo reading is on), assuming numbers and dates are written as in the UI language.
 */
export function usePhotoSession(vehicleId: string, purpose?: ReadingPurpose): PhotoSession {
  const { i18n } = useTranslation()
  const queue = usePhotoQueue(vehicleId, purpose ? { purpose, locale: i18n.language } : undefined)
  const [leftOut, setLeftOut] = useState(0)
  const [saving, setSaving] = useState(false)

  const reset = () => {
    queue.discard()
    setLeftOut(0)
  }

  const submit = async (save: (photoIds: string[]) => Promise<Saved>, editing: boolean) => {
    const ids = editing ? [] : queue.ids
    setSaving(true)
    try {
      const saved = await save(ids)
      if (editing) return true
      queue.forget() // they are the log's photos now (or, if not attached, drafts that expire)
      const missing = saved ? ids.length - saved.photoCount : 0
      if (missing <= 0) return true
      setLeftOut(missing)
      return false
    } finally {
      setSaving(false)
    }
  }

  return { queue, leftOut, saving, submit, reset }
}
