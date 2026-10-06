import Table from '@mui/material/Table'
import TableContainer from '@mui/material/TableContainer'
import type { ReactNode } from 'react'
import { glass } from '../theme/components.ts'
import { visuallyHidden } from './visuallyHidden.ts'

/**
 * A table on a panel of its own, with a caption for screen readers; it scrolls sideways by itself when it is wider than the screen.
 * Rows go in `children` (TableHead, TableBody); a row's first cell names it (`component="th" scope="row"`).
 */
export function SurfaceTable({ caption, children }: { caption?: string; children: ReactNode }) {
  return (
    <TableContainer sx={(theme) => ({ ...glass(theme), borderRadius: '12px', boxShadow: `0 0 0 1px ${theme.vars.palette.divider}` })}>
      <Table>
        {caption && <caption style={visuallyHidden}>{caption}</caption>}
        {children}
      </Table>
    </TableContainer>
  )
}
