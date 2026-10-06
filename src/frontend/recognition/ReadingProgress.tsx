import CircularProgress from '@mui/material/CircularProgress'
import LinearProgress from '@mui/material/LinearProgress'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'

/**
 * While a photo is being read: says so where the user looks before saving, and whether they can save right away (what they leave empty is
 * then filled in from the photo, and the log is marked for review). Goes inside the dialog's reading status region. With the wait's
 * window (`since`..`until`), a bar shows how much of it has passed; the words are what a screen reader hears.
 */
export function ReadingProgress({ active, canSave, since, until }: { active: boolean; canSave: boolean; since?: number | null; until?: number | null }) {
  const { t } = useTranslation()
  if (!active) return null
  return (
    <Stack className="tk-appear" sx={{ gap: 0.75, my: 0.5 }}>
      <Stack direction="row" sx={{ alignItems: 'center', gap: 1 }}>
        <CircularProgress size={14} color="inherit" aria-hidden />
        <Typography variant="body2">
          {t('reading.progress')}
          {canSave && ` ${t('reading.saveNow')}`}
        </Typography>
      </Stack>
      {since != null && until != null && until > since && <WaitBar since={since} until={until} />}
    </Stack>
  )
}

/** How much of the reading's waiting window has passed, moved on every second. */
function WaitBar({ since, until }: { since: number; until: number }) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const tick = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(tick)
  }, [])
  const passed = Math.min(100, Math.max(0, ((now - since) / (until - since)) * 100))
  return <LinearProgress variant="determinate" value={passed} aria-hidden data-testid="reading-wait" />
}
