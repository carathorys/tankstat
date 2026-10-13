import Box from '@mui/material/Box'
import Typography from '@mui/material/Typography'
import { Fragment, type ReactNode } from 'react'

/**
 * Facts as label and value pairs (a description list). On a phone each label stands above its value, as a form field's does; wider,
 * the labels are a column of their own, muted, at most 40% of the list (a longer label wraps), never narrower than 7.5rem, so short
 * labels line their values up as before. A value wraps anywhere rather than run past its panel (an address, an id, a path), and each
 * label sits on the first line of its value.
 */
export function DefinitionList({ items }: { items: readonly { label: string; value: ReactNode }[] }) {
  return (
    <Box
      component="dl"
      sx={{
        display: 'grid',
        gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: 'fit-content(40%) minmax(0, 1fr)' },
        columnGap: 2,
        rowGap: { xs: 0.25, sm: 2 },
        alignItems: 'baseline',
        m: 0,
      }}
    >
      {items.map((item) => (
        <Fragment key={item.label}>
          <Typography component="dt" variant="body2" sx={{ color: 'text.secondary', minWidth: { sm: '7.5rem' } }}>
            {item.label}
          </Typography>
          <Typography
            component="dd"
            variant="body2"
            sx={{ m: 0, minWidth: 0, overflowWrap: 'anywhere', '&:not(:last-of-type)': { mb: { xs: 1.5, sm: 0 } } }}
          >
            {item.value}
          </Typography>
        </Fragment>
      ))}
    </Box>
  )
}
