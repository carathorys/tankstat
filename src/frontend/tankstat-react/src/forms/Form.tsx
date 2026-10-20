import { useEffect, useMemo, useRef, type FormEvent, type FormHTMLAttributes, type ReactNode, type Ref } from 'react'
import { useErrorText } from '../i18n/errors.ts'
import { FormContext, type FieldHandle, type FormRegistry } from './formContext.ts'
import { fieldOf } from './refusals.ts'

/**
 * A form that checks its Fields the way the browser does (required, e-mail, each Field's own checks) before `onSubmit` runs: a bad field
 * shows its message, the first one takes the focus, and nothing is sent. The browser's own bubbles never show (`noValidate`). `refusal`:
 * what the server answered the last submit with; when it is about one field (a required value, `refusals.ts`), that field says it and takes
 * the focus, and an `ErrorMessage` of the same error inside the form says nothing.
 */
export function Form({
  onSubmit,
  refusal,
  ref,
  children,
  ...rest
}: Omit<FormHTMLAttributes<HTMLFormElement>, 'onSubmit' | 'noValidate'> & {
  onSubmit: (event: FormEvent<HTMLFormElement>) => void
  refusal?: unknown
  ref?: Ref<HTMLFormElement>
  children: ReactNode
}) {
  const errorText = useErrorText()
  const handles = useRef<Map<Element, FieldHandle>>(null)
  handles.current ??= new Map()
  const field = fieldOf(refusal)
  const said = field === undefined ? undefined : errorText(refusal)
  const handleOf = (name: string) => [...handles.current!].find(([control]) => control.getAttribute('name') === name)?.[1]
  const registry = useMemo<FormRegistry>(
    () => ({
      register: (control, handle) => {
        handles.current!.set(control, handle)
        return () => void handles.current!.delete(control)
      },
      refusalFor: (name) => (name === field ? said : undefined),
      says: (error) => error !== undefined && error === refusal && field !== undefined && handleOf(field) !== undefined,
    }),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- handleOf reads the registry's own map
    [field, said, refusal],
  )
  // A refusal about one field moves the focus there, as the form's own checks do for the first bad field.
  useEffect(() => {
    if (field) handleOf(field)?.focus()
    // eslint-disable-next-line react-hooks/exhaustive-deps -- once per refusal
  }, [refusal])

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
      <form ref={ref} noValidate onSubmit={submit} {...rest}>
        {children}
      </form>
    </FormContext.Provider>
  )
}
