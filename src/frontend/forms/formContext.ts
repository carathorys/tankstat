import { createContext } from 'react'

/** A field as its form sees it: bring its own checks up to date before the form validates, and take the focus when it is the first bad one. */
export interface FieldHandle {
  refresh: () => void
  focus: () => void
}

/** The fields of a Form, by the native element the browser validates for each. */
export interface FormRegistry {
  register: (control: Element, handle: FieldHandle) => () => void
}

export const FormContext = createContext<FormRegistry | null>(null)
