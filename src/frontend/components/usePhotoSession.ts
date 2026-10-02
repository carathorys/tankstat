import { useRef, useState } from 'react'
import type { LogKind } from '../pictures/upload.ts'
import { usePhotoQueue, type PhotoQueue, type Saved } from './usePhotoQueue.ts'

export interface PhotoSession {
  queue: PhotoQueue
  /** Set when a new log was saved but some of its photos could not be sent: the dialog then shows that log's photos instead of the form. */
  savedId: string | undefined
  failure: unknown
  /** True from pressing Save until the log and its photos went through; the dialog must stay open and the photos untouched meanwhile. */
  saving: boolean
  /** Saves the log and, for a new one, sends the queued photos. Resolves to true when the dialog may close. */
  submit: (save: () => Promise<Saved>, editing: boolean) => Promise<boolean>
  /** Forget everything (the dialog was closed). */
  reset: () => void
}

/** The photo part of the add/edit dialogs of refuelings and expenses: the queue, the save-then-upload step and its leftovers. */
export function usePhotoSession(kind: LogKind): PhotoSession {
  const queue = usePhotoQueue()
  const [savedId, setSavedId] = useState<string>()
  const [failure, setFailure] = useState<unknown>()
  const [saving, setSaving] = useState(false)
  // Bumped by reset(): a save that finishes after the dialog was closed must not bring its leftovers back into the next one.
  const session = useRef(0)

  const reset = () => {
    session.current++
    queue.clear()
    setSavedId(undefined)
    setFailure(undefined)
  }

  const submit = async (save: () => Promise<Saved>, editing: boolean) => {
    const started = session.current
    setSaving(true)
    try {
      const saved = await save()
      if (editing || !saved || queue.items.length === 0) return true
      const error = await queue.uploadAll(kind, saved.id)
      if (error === undefined || started !== session.current) return true
      setFailure(error)
      setSavedId(saved.id)
      return false
    } finally {
      setSaving(false)
    }
  }

  return { queue, savedId, failure, saving, submit, reset }
}
