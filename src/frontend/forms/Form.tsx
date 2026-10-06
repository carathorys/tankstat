import { useMemo, useRef, type FormEvent, type FormHTMLAttributes, type ReactNode } from 'react'
import { FormContext, type FieldHandle, type FormRegistry } from './formContext.ts'

/**
 * A form that checks its Fields the way the browser does (required, e-mail, each Field's own checks) before `onSubmit` runs: a bad field
 * shows its message, the first one takes the focus, and nothing is sent. The browser's own bubbles never show (`noValidate`).
 */
export function Form({
  onSubmit,
  children,
  ...rest
}: Omit<FormHTMLAttributes<HTMLFormElement>, 'onSubmit' | 'noValidate'> & {
  onSubmit: (event: FormEvent<HTMLFormElement>) => void
  children: ReactNode
}) {
  const handles = useRef<Map<Element, FieldHandle>>(null)
  handles.current ??= new Map()
  const registry = useMemo<FormRegistry>(
    () => ({
      register: (control, handle) => {
        handles.current!.set(control, handle)
        return () => void handles.current!.delete(control)
      },
    }),
    [],
  )

  function submit(event: FormEvent<HTMLFormElement>) {
    const form = event.currentTarget
    for (const handle of handles.current!.values()) handle.refresh() // a photo may have changed a value without any event
    if (!form.checkValidity()) {
      // checkValidity fired `invalid` on each bad control, so their messages show; the first one gets the focus.
      event.preventDefault()
      const first = [...form.elements].find((element) => 'validity' in element && !(element as HTMLInputElement).validity.valid)
      if (first) (handles.current!.get(first)?.focus ?? (() => (first as HTMLElement).focus()))()
      return
    }
    onSubmit(event)
  }

  return (
    <FormContext.Provider value={registry}>
      <form noValidate onSubmit={submit} {...rest}>
        {children}
      </form>
    </FormContext.Provider>
  )
}
