import FormLabel from '@mui/material/FormLabel'
import MenuItem from '@mui/material/MenuItem'
import Select from '@mui/material/Select'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useId } from 'react'

/**
 * A labelled choice (units, fuel, a chart's metric, a person): the label names the select (aria-labelledby), the options open in a list.
 * An empty `value` shows the `placeholder`.
 */
export function LabeledSelect<T extends string>({
  label,
  value,
  options,
  onChange,
  disabled,
  placeholder,
}: {
  label: string
  value: T
  options: { value: T; label: string }[]
  onChange: (value: T) => void
  disabled?: boolean
  placeholder?: string
}) {
  const labelId = useId()
  return (
    <Stack sx={{ gap: 0.5, minWidth: 0 }}>
      <FormLabel id={labelId}>{label}</FormLabel>
      <Select<T>
        labelId={labelId}
        value={value}
        onChange={(event) => onChange(event.target.value as T)}
        disabled={disabled}
        displayEmpty={placeholder !== undefined}
        renderValue={(selected) =>
          selected ? options.find((o) => o.value === selected)?.label : <Typography component="span" variant="body2" sx={{ color: 'text.secondary' }}>{placeholder}</Typography>
        }
      >
        {options.map((o) => (
          <MenuItem key={o.value} value={o.value}>
            {o.label}
          </MenuItem>
        ))}
      </Select>
    </Stack>
  )
}
