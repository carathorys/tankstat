import Box from '@mui/material/Box'
import Stack from '@mui/material/Stack'
import { Gauge, gaugeClasses } from '@mui/x-charts/Gauge'
import { CalendarClock, Milestone } from 'lucide-react'
import type { RecurrenceState } from '../gql/generated.ts'
import { GAUGE_GAP, GAUGE_SIZE, RECURRENCE_TONE, type LimitProgress } from '../recurringProgress.ts'

/**
 * A small dial per limit of a schedule (a calendar for time, a milestone for distance): how much of the interval has passed, in the colour
 * of its state; the limit that decides at full strength, the other faded. A picture only: the state and the due text beside it say the
 * same in words.
 */
export function ProgressGauges({ progress, state }: { progress: readonly LimitProgress[]; state: RecurrenceState }) {
  return (
    <Stack direction="row" aria-hidden sx={{ gap: `${GAUGE_GAP}px` }}>
      {progress.map((p) => {
        const Icon = p.limit === 'TIME' ? CalendarClock : Milestone
        return (
          <Box key={p.limit} data-limit={p.limit} data-deciding={p.deciding} sx={{ position: 'relative', width: GAUGE_SIZE, height: GAUGE_SIZE, opacity: p.deciding ? 1 : 0.45 }}>
            <Gauge
              width={GAUGE_SIZE}
              height={GAUGE_SIZE}
              value={Math.round(Math.min(Math.max(p.used, 0), 1) * 100)}
              startAngle={-120}
              endAngle={120}
              innerRadius="74%"
              margin={0} // the dial fills its box; the default margins would leave a speck
              text=""
              skipAnimation
              sx={(theme) => ({
                [`& .${gaugeClasses.valueArc}`]: { fill: theme.vars.palette[RECURRENCE_TONE[state]].main },
                [`& .${gaugeClasses.referenceArc}`]: { fill: theme.vars.palette.neutral.soft },
              })}
            />
            <Box sx={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'text.secondary' }}>
              <Icon size={14} />
            </Box>
          </Box>
        )
      })}
    </Stack>
  )
}
