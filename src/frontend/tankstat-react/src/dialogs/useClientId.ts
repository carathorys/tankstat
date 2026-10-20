import { useState } from 'react'
import { uuidV4 } from '../offline/uuid.ts'

/**
 * The id an add dialog gives what it creates: one per opening, the same for every Save of that opening. So a Save tried again after an
 * answer that never arrived finds what the first one created on the server instead of creating it twice; the next opening is a new entry.
 */
export function useClientId(open: boolean): string {
  const [id, setId] = useState(uuidV4)
  const [wasOpen, setWasOpen] = useState(open)
  if (open !== wasOpen) {
    setWasOpen(open)
    if (open) setId(uuidV4())
  }
  return id
}
