import Autocomplete, { type AutocompleteRenderInputParams } from '@mui/material/Autocomplete'
import OutlinedInput from '@mui/material/OutlinedInput'
import { useForkRef } from '@mui/material/utils'
import { useState, type Ref } from 'react'
import { useFieldControl } from './fieldContext.ts'

const contains = (option: string, typed: string) => option.toLowerCase().includes(typed.toLowerCase())

/**
 * The text control of a Field that suggests values as the user types (free text is fine): earlier categories, currencies. The listbox is
 * named by the field's label. Controlled with `value` + `onChange`, or uncontrolled with `defaultValue`.
 */
export function FieldAutocomplete({
  options,
  value,
  defaultValue = '',
  onChange,
  maxLength,
  uppercase,
  openOnFocus = false,
  optionLabel,
  filter = contains,
}: {
  options: readonly string[]
  value?: string
  defaultValue?: string
  onChange?: (value: string) => void
  maxLength?: number
  /** Show the value in capitals (a currency code). */
  uppercase?: boolean
  /** Offer every option as soon as the field has the focus (a short list). */
  openOnFocus?: boolean
  /** How an option reads in the list (default: the option itself), e.g. "HUF – Hungarian forint". */
  optionLabel?: (option: string) => string
  /** Which options suit what was typed (default: those that contain it, ignoring case). */
  filter?: (option: string, typed: string) => boolean
}) {
  const field = useFieldControl()
  const [own, setOwn] = useState(defaultValue)
  const text = value ?? own

  return (
    <Autocomplete
      freeSolo
      disableClearable
      openOnFocus={openOnFocus}
      id={field.id} // the listbox is then labelled by the Field's label (`${id}-label`)
      options={options}
      inputValue={text}
      onInputChange={(_, next) => {
        if (value === undefined) setOwn(next)
        onChange?.(next)
      }}
      filterOptions={(all, { inputValue }) => (inputValue.trim() === '' ? [...all] : all.filter((o) => filter(o, inputValue.trim())))}
      renderOption={({ key, ...props }, option) => (
        <li key={key} {...props}>
          {optionLabel ? optionLabel(option) : option}
        </li>
      )}
      renderInput={(params) => <AutocompleteInput params={params} maxLength={maxLength} uppercase={uppercase} />}
    />
  )
}

/** The input of a FieldAutocomplete: the autocomplete's wiring plus the Field's name, messages and validation. */
function AutocompleteInput({ params, maxLength, uppercase }: { params: AutocompleteRenderInputParams; maxLength?: number; uppercase?: boolean }) {
  const field = useFieldControl()
  const { ref: autocompleteRef, ...htmlInput } = params.slotProps.htmlInput as typeof params.slotProps.htmlInput & { ref?: Ref<HTMLInputElement> }
  const ref = useForkRef(autocompleteRef, field.bindControl)
  return (
    <OutlinedInput
      {...params.slotProps.input}
      fullWidth
      name={field.name}
      aria-describedby={field.describedBy}
      inputRef={ref}
      // inputProps, not slotProps.input: InputBase calls only inputProps' onChange/onFocus/onBlur, which the autocomplete needs.
      inputProps={{ ...htmlInput, maxLength, ...(uppercase ? { style: { textTransform: 'uppercase' } } : {}) }}
    />
  )
}
