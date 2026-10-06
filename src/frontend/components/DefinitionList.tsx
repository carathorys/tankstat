import Box from '@mui/material/Box'
import Typography from '@mui/material/Typography'
import { Fragment, type ReactNode } from 'react'

/** Facts as label and value pairs (a description list): the labels in a column of their own, muted, the values beside them. */
export function DefinitionList({ items }: { items: readonly { label: string; value: ReactNode }[] }) {
  return (
    <Box component="dl" sx={{ display: 'grid', gridTemplateColumns: 'minmax(7.5rem, max-content) minmax(0, 1fr)', columnGap: 2, rowGap: 2, alignItems: 'center', m: 0 }}>
      {items.map((item) => (
        <Fragment key={item.label}>
          <Typography component="dt" variant="body2" sx={{ color: 'text.secondary' }}>
            {item.label}
          </Typography>
          <Typography component="dd" variant="body2" sx={{ m: 0 }}>
            {item.value}
          </Typography>
        </Fragment>
      ))}
    </Box>
  )
}
