import { useQuery } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Link from '@mui/material/Link'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { SegmentedChecks } from '../components/Segmented.tsx'
import { todayIso } from '../forms/dates.ts'
import { TyreRoughEstimateDocument, type DistanceUnit, type TyreSeason } from '../gql/generated.ts'
import { useDebouncedValue } from '../hooks/useDebouncedValue.ts'
import { useFormat } from '../i18n/format.ts'
import { ErrorMessage } from '../messages.tsx'
import { useConnectivity } from '../offline/useConnectivity.ts'
import { standaloneLink } from '../theme/components.ts'
import { isSeasons, SEASON_MONTHS, sortedMonths } from './tyreMonths.ts'
import { useTyreWords } from './tyres.ts'

const ALL = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]

/**
 * A set's usual months (#291, the owner's decisions 49 and 53): twelve toggles, short names seen and long ones said, the season's ticked
 * until changed; changed, a hint says so and *Season's months* goes back. The last month cannot be unticked (a set is on in some month).
 */
export function UsualMonths({ season, months, onChange }: { season: TyreSeason; months: readonly number[]; onChange: (months: number[]) => void }) {
  const { t } = useTranslation()
  const format = useFormat()
  const changed = !isSeasons(season, months)
  return (
    <Stack sx={{ gap: 0.5, minWidth: 0 }}>
      <Typography variant="body2" component="span" sx={{ fontWeight: 600 }}>
        {t('tyres.estimate.months')}
      </Typography>
      <SegmentedChecks
        label={t('tyres.estimate.months')}
        values={months.map(String)}
        // The short name is seen, the long one names the checkbox (and is its tooltip), as an icon segment's words do.
        options={ALL.map((m) => ({ value: String(m), label: format.month(m), icon: <span style={{ padding: '0 6px' }}>{format.month(m, 'short')}</span> }))}
        onChange={(month, on) => {
          const next = sortedMonths(on ? [...months, Number(month)] : months.filter((m) => m !== Number(month)))
          if (next.length > 0) onChange(next)
        }}
      />
      <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
        <Typography variant="caption" sx={{ color: 'text.secondary' }}>
          {changed ? t('tyres.estimate.changed') : t('tyres.estimate.monthsHint')}
        </Typography>
        {changed && (
          <Link component="button" type="button" variant="caption" sx={standaloneLink} onClick={() => onChange([...SEASON_MONTHS[season]])}>
            {t('tyres.estimate.seasons')}
          </Link>
        )}
      </Stack>
    </Stack>
  )
}

/**
 * *Estimate from the fuel log* (#291): under a used set's rough figure, in the add form and in the rough figure dialog. Pressed, a panel
 * shows the set's usual months and what the server works out for them over `since`..`until` (the day the figure stops covering: the
 * caller's, so the estimate and the save never disagree on it), with what the fuel log cannot cover and the days another set was on. It is
 * only offered: *Use it* hands the figure to the field, which stays editable (decision 52). Asked again, a little later, when *in use
 * since* or a month changes. The server works it out, so without it the button is off and says why (O1).
 */
export function RoughEstimate({
  vehicleId,
  unit,
  season,
  since,
  until,
  months,
  onMonths,
  onUse,
}: {
  vehicleId: string
  unit: DistanceUnit
  season: TyreSeason
  /** *In use since* as typed (an ISO day), empty while it is not. */
  since: string
  /** The first day the figure does not cover; null: today. */
  until: string | null
  months: readonly number[]
  onMonths: (months: number[]) => void
  onUse: (distance: number) => void
}) {
  const { t } = useTranslation()
  const format = useFormat()
  const words = useTyreWords(unit)
  const { reachable } = useConnectivity()
  const noteId = useId()
  const [asked, setAsked] = useState(false)
  const end = until ?? todayIso()
  const valid = /^\d{4}-\d{2}-\d{2}$/.test(since) && since <= end
  const wanted = valid ? { vehicleId, since, until: end, months: [...months] } : null
  // What is asked follows what is typed a little later, so a month tapped after another asks once.
  const key = wanted ? JSON.stringify(wanted) : ''
  const settled = useDebouncedValue(key, 350)
  const variables = settled ? (JSON.parse(settled) as NonNullable<typeof wanted>) : undefined
  const { data, error, loading } = useQuery(TyreRoughEstimateDocument, {
    variables: variables!,
    skip: !asked || !variables || !reachable,
    fetchPolicy: 'no-cache', // worked out afresh every time: the readings change under it
  })
  const estimate = asked && variables && settled === key ? data?.tyreRoughEstimate : undefined
  // Something to offer: a run that counted nothing (the fuel log ends before the months, or stood still in them) is "nothing in the months".
  const counted = !!estimate?.readings && !!estimate.from && estimate.distance > 0
  const why = !reachable ? t('tyres.estimate.offline') : !valid ? t('tyres.estimate.needsSince') : undefined

  const monthsText =
    months.length === 12 ? t('tyres.estimate.wholeYear') : isSeasons(season, months) ? t('tyres.estimate.theSeasons') : format.list(months.map((m) => format.month(m, 'short')))

  return (
    <Stack sx={{ gap: 1, minWidth: 0 }}>
      <Stack sx={{ gap: 0.5, alignItems: 'flex-start' }}>
        <Button type="button" variant="soft" disabled={why !== undefined} aria-describedby={why ? noteId : undefined} aria-expanded={asked} onClick={() => setAsked(true)}>
          {t('tyres.estimate.button')}
        </Button>
        {why && (
          <Typography id={noteId} variant="caption" sx={{ color: 'text.secondary' }}>
            {why}
          </Typography>
        )}
      </Stack>
      {asked && reachable && valid && (
        <Stack sx={{ gap: 1, p: 1.5, borderRadius: 2, bgcolor: 'action.hover', minWidth: 0 }}>
          <UsualMonths season={season} months={months} onChange={onMonths} />
          <div role="status">
            {error ? (
              <ErrorMessage error={error} />
            ) : !estimate ? (
              loading || settled !== key ? (
                <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                  {t('tyres.estimate.working')}
                </Typography>
              ) : null
            ) : !estimate.readings ? (
              <Typography variant="body2">{t('tyres.estimate.noReading')}</Typography>
            ) : (
              <Stack sx={{ gap: 0.5 }}>
                <Typography variant="body2">
                  {counted
                    ? t('tyres.estimate.result', {
                        distance: words.distance(estimate.distance),
                        months: monthsText,
                        from: format.monthOfYear(estimate.from!),
                        until: until ? format.date(until) : t('tyres.estimate.today'),
                      })
                    : t('tyres.estimate.nothing')}
                </Typography>
                {estimate.notCovered.length > 0 && (
                  <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                    {t('tyres.estimate.notCovered', { months: format.list(estimate.notCovered.map((r) => format.monthsBetween(r.from, r.until))) })}
                  </Typography>
                )}
                {estimate.leftOutDays > 0 && (
                  <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                    {t('tyres.estimate.leftOut', { count: estimate.leftOutDays })}
                  </Typography>
                )}
              </Stack>
            )}
          </div>
          {estimate && counted && (
            <Button
              type="button"
              variant="soft"
              sx={{ alignSelf: 'flex-start' }}
              aria-label={t('tyres.estimate.useAria', { distance: words.distance(estimate.distance) })}
              onClick={() => onUse(estimate.distance)}
            >
              {t('tyres.estimate.use')}
            </Button>
          )}
        </Stack>
      )}
    </Stack>
  )
}
