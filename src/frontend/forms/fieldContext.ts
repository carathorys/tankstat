import { createContext, useContext } from 'react'

/** What a Field hands to its control (FieldInput, FieldDate, FieldAutocomplete): ids, its messages, and how to take part in validation. */
export interface FieldControl {
  /** The control's id; the label points at it (unless the control is composite). */
  id: string
  name: string
  labelId: string
  /** The ids of the field's messages (hint, notes such as what a photo showed, the error), in that order. */
  describedBy: string | undefined
  /** An error message about the value is shown. */
  invalid: boolean
  required: boolean
  /** Gives the field the native element the browser validates (an input, or a hidden one behind a composite control); a callback ref. */
  bindControl: (element: HTMLInputElement | HTMLTextAreaElement | null) => void
  /**
   * A composite control (a date picker) is named through aria-labelledby, not the label's `for`; it focuses its own visible part when it is
   * the first bad field, and may know a problem of its own (a partly typed day), checked like a failed `invalid` test. Returns the undo.
   */
  composite: (control: CompositeControl) => () => void
}

export interface CompositeControl {
  focus: () => void
  problem: () => string | null
}

/** How a message joins the field's description (FieldMessage). */
export interface FieldMessages {
  register: (id: string, slot: number) => () => void
}

export const FieldContext = createContext<(FieldControl & FieldMessages) | null>(null)

export function useFieldControl(): FieldControl {
  const field = useContext(FieldContext)
  if (!field) throw new Error('A field control must be inside a Field.')
  return field
}
