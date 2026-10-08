import Box from '@mui/material/Box'
import IconButton from '@mui/material/IconButton'
import Link from '@mui/material/Link'
import Popover from '@mui/material/Popover'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import { Check, CloudUpload, Pencil, Plus, Trash2, TriangleAlert, Undo2, type LucideIcon } from 'lucide-react'
import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link as RouterLink } from 'react-router'
import { useKeyText } from '../i18n/errors.ts'
import type { RowSyncState } from '../offline/useRowSyncStates.ts'
import { useConnectivity } from '../offline/useConnectivity.ts'
import type { Tone } from '../theme/components.ts'
import { visuallyHidden } from './visuallyHidden.ts'

const LOOKS: Record<RowSyncState['kind'], { tone: Tone; Icon: LucideIcon }> = {
  new: { tone: 'success', Icon: Plus },
  changed: { tone: 'info', Icon: Pencil },
  deleted: { tone: 'warning', Icon: Trash2 },
  restored: { tone: 'info', Icon: Undo2 },
  done: { tone: 'success', Icon: Check },
  parked: { tone: 'error', Icon: TriangleAlert },
}

/** The header of a list's sync column: an icon, its name for screen readers. */
export function SyncStateHeader() {
  const { t } = useTranslation()
  return (
    <Box component="span" sx={{ display: 'inline-flex', color: 'text.secondary' }}>
      <CloudUpload size={18} aria-hidden />
      <span style={visuallyHidden}>{t('offline.state.column')}</span>
    </Box>
  )
}

/**
 * A row's state in a list's sync column: an icon in the state's colour (the shape says it too, never the colour alone) whose name is the
 * whole state, opening a bubble in the same colour that says why and what happens next: a change waiting on this device, or the reason the
 * server gave for one it could not apply. The bubble only explains and links to Waiting to sync, where the changes are decided on; a row
 * waiting to be trashed keeps its own Keep button.
 */
export function SyncStateButton({ state, name }: { state: RowSyncState; name: string }) {
  const { t } = useTranslation()
  const keyText = useKeyText()
  const { reachable } = useConnectivity()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const titleId = useId()
  const { tone, Icon } = LOOKS[state.kind]
  const title = state.kind === 'parked' ? t('offline.state.parked') : t(`offline.pending.${state.kind}`)
  const reason =
    state.kind === 'parked'
      ? state.parked.reason
        ? (keyText(state.parked.reason.key, Object.fromEntries(state.parked.reason.args.map((a) => [a.name, a.value]))) ?? state.parked.reason.key)
        : ''
      : t(`offline.state.reason.${state.kind}`)
  const label = t('offline.state.aria', { state: title, name })

  return (
    <>
      <Tooltip title={title} describeChild>
        <IconButton
          aria-label={label}
          aria-haspopup="dialog"
          aria-expanded={anchor !== null}
          onClick={(e) => setAnchor(e.currentTarget)}
          sx={(theme) => ({
            width: 44,
            height: 44,
            color: theme.vars.palette[tone].softText,
            '& .tk-state': { backgroundColor: theme.vars.palette[tone].soft },
            '&:hover .tk-state': { backgroundColor: theme.vars.palette[tone].softHover },
          })}
        >
          <Box className="tk-state" sx={{ width: 28, height: 28, borderRadius: '50%', display: 'grid', placeItems: 'center' }}>
            <Icon size={15} aria-hidden />
          </Box>
        </IconButton>
      </Tooltip>
      <Popover
        open={anchor !== null}
        anchorEl={anchor}
        onClose={() => setAnchor(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
        slotProps={{
          paper: {
            role: 'dialog',
            'aria-labelledby': titleId,
            sx: (theme) => ({
              maxWidth: 320,
              p: 1.5,
              mt: 0.5,
              borderRadius: 1.5,
              border: `1px solid ${theme.vars.palette[tone].main}`,
              // The state's tint over the paper, so the bubble is the colour of its icon in either scheme.
              backgroundImage: `linear-gradient(${theme.vars.palette[tone].soft}, ${theme.vars.palette[tone].soft})`,
            }),
          },
        }}
      >
        <Typography id={titleId} component="h2" variant="subtitle2" sx={{ display: 'flex', alignItems: 'center', gap: 0.75, color: `${tone}.softText` }}>
          <Icon size={15} aria-hidden />
          {title}
        </Typography>
        {reason && (
          <Typography variant="body2" sx={{ mt: 0.5 }}>
            {reason}
          </Typography>
        )}
        {!reachable && state.kind !== 'parked' && (
          <Typography variant="body2" sx={{ mt: 0.5, color: 'text.secondary' }}>
            {t('offline.state.unreachable')}
          </Typography>
        )}
        <Link component={RouterLink} to="/sync" variant="body2" onClick={() => setAnchor(null)} sx={{ display: 'inline-flex', alignItems: 'center', minHeight: 44, mt: 0.5, fontWeight: 600 }}>
          {t('offline.state.open')}
        </Link>
      </Popover>
    </>
  )
}
