import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import Popover from '@mui/material/Popover'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import type { ParseKeys } from 'i18next'
import { ArrowDown, ArrowUp, Columns3, RotateCcw } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { IconAction } from '../components/IconAction.tsx'
import { COARSE } from '../theme/components.ts'

export interface ColumnChoice {
  id: string
  label: ParseKeys
  hideable: boolean
  visible: boolean
  toggle: (visible: boolean) => void
}

/** Show/hide and reorder columns. Up/down buttons (not drag and drop) so it works with touch and keyboard. */
export function ColumnsPopover({
  columns,
  onMove,
  onReset,
}: {
  /** In current display order. */
  columns: ColumnChoice[]
  onMove: (id: string, offset: -1 | 1) => void
  onReset: () => void
}) {
  const { t } = useTranslation()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const title = t('grid.columns')

  return (
    <>
      <IconAction label={title} aria-haspopup="dialog" aria-expanded={anchor !== null} onClick={(e) => setAnchor(e.currentTarget)}>
        <Columns3 size={18} aria-hidden />
      </IconAction>
      <Popover
        open={anchor !== null}
        anchorEl={anchor}
        onClose={() => setAnchor(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
        slotProps={{ paper: { role: 'dialog', 'aria-label': title, sx: { width: 320, maxWidth: '92vw', p: 2, mt: 1 } } }}
      >
        <Typography variant="body2" sx={{ color: 'text.secondary', mb: 1.5 }}>
          {t('grid.columnsHint')}
        </Typography>
        <Stack sx={{ gap: 0.25 }}>
          {columns.map((c, i) => (
            <Stack key={c.id} direction="row" sx={{ alignItems: 'center', gap: 1 }}>
              <Checkbox
                size="small"
                sx={{ p: 0.5, ml: -0.5, [COARSE]: { p: 1.5 } }} // compact rows; a touch screen keeps a 44 px target
                slotProps={{ input: { 'aria-label': t('grid.showColumn', { column: t(c.label) }) } }}
                checked={c.visible}
                disabled={!c.hideable}
                onChange={(e) => c.toggle(e.target.checked)}
              />
              <Typography variant="body2" sx={{ flex: 1 }}>
                {t(c.label)}
              </Typography>
              <IconAction size="small" variant="ghost" disabled={i === 0} label={t('grid.moveUp', { column: t(c.label) })} onClick={() => onMove(c.id, -1)}>
                <ArrowUp size={16} aria-hidden />
              </IconAction>
              <IconAction size="small" variant="ghost" disabled={i === columns.length - 1} label={t('grid.moveDown', { column: t(c.label) })} onClick={() => onMove(c.id, 1)}>
                <ArrowDown size={16} aria-hidden />
              </IconAction>
            </Stack>
          ))}
        </Stack>
        <Button size="small" variant="soft" color="neutral" sx={{ mt: 2 }} onClick={onReset}>
          <RotateCcw size={14} aria-hidden />
          {t('grid.reset')}
        </Button>
      </Popover>
    </>
  )
}
