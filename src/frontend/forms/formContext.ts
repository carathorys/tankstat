import { createContext } from 'react'

/** A field as its form sees it: bring its own checks up to date before the form validates, and take the focus when it is the first bad one. */
export interface FieldHandle {
  refresh: () => void
  focus: () => void
}

/** The fields of a Form, by the native element the browser validates for each. */
export interface FormRegistry {
  register: (control: Element, handle: FieldHandle) => () => void
  /** What the server refused about the field of this `name` at the last submit, in words; said at the field. */
  refusalFor: (name: string) => string | undefined
  /** Whether a field of the form says this error already, so a message under the form would say it twice. */
  says: (error: unknown) => boolean
}

export const FormContext = createContext<FormRegistry | null>(null)
