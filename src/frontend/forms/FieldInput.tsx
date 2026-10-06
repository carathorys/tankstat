import OutlinedInput, { type OutlinedInputProps } from '@mui/material/OutlinedInput'
import type { HTMLAttributes } from 'react'
import { useFieldControl } from './fieldContext.ts'

export interface FieldInputProps extends Omit<OutlinedInputProps, 'id' | 'name' | 'error' | 'required' | 'inputRef' | 'aria-describedby'> {
  maxLength?: number
  inputMode?: HTMLAttributes<HTMLInputElement>['inputMode']
  /** Show the value in capitals (a currency code); what is sent is the value as typed. */
  uppercase?: boolean
}

/** The text control of a Field (`multiline` for a longer note): its id, name, required flag and messages come from the Field. */
export function FieldInput({ maxLength, inputMode, uppercase, slotProps, ...rest }: FieldInputProps) {
  const field = useFieldControl()
  return (
    <OutlinedInput
      fullWidth
      {...rest}
      id={field.id}
      name={field.name}
      inputRef={field.bindControl}
      aria-describedby={field.describedBy}
      slotProps={{
        ...slotProps,
        input: { maxLength, inputMode, ...(uppercase ? { style: { textTransform: 'uppercase' } } : {}), ...slotProps?.input },
      }}
    />
  )
}
