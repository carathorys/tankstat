import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Chip from '@mui/material/Chip'
import Stack from '@mui/material/Stack'
import type { Theme } from '@mui/material/styles'
import Typography from '@mui/material/Typography'
import { CheckCheck, ChevronRight, Fuel, Receipt } from 'lucide-react'
import { useEffect, useLayoutEffect, useRef, useState, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { LazySparkline } from '../dashboard/LazySparkline.tsx'
import { ExpenseFormDialog } from '../ExpenseFormDialog.tsx'
import type { VehicleCardFieldsFragment } from '../gql/generated.ts'
import { useDueText } from '../hooks/useDueText.ts'
import { useMediaQuery } from '../hooks/useMediaQuery.ts'
import { useFormat } from '../i18n/format.ts'
import { RecurringDoneDialog } from '../RecurringDoneDialog.tsx'
import { RefuelingFormDialog } from '../RefuelingFormDialog.tsx'
import { preselect } from '../recurringDone.ts'
import { spentText } from '../spending.ts'
import { MEDIA } from '../theme/media.ts'
import { canLogFor } from '../vehicles.ts'
import { cardClickOrigin, tappedAway } from './cardClicks.ts'
import { CoverLayers } from './CoverLayers.tsx'
import { IconAction } from './IconAction.tsx'
import { RecurringStatusBadge } from './RecurringStatus.tsx'
import { useCardActions } from './useCardActions.ts'
import { UserChip } from './UserAvatar.tsx'
import { outbox, usePendingCount } from '../offline/outbox.ts'
import { PendingBadge } from './PendingBadge.tsx'

type Vehicle = VehicleCardFieldsFragment

const TEXT_SHADOW = '0 1px 4px rgba(0,0,0,0.7)'

/**
 * The card's look. It is never shorter than the top block plus the figures (the component measures --top-h and --panel-h), so revealing
 * the figures covers nothing; the list item is a grid, so the cards of a row stretch to the tallest one and nothing moves while one
 * reveals. The figures and the trend slide up from the bottom edge: on hover where there is a pointer, on keyboard focus anywhere in the
 * card (:focus-visible, not :focus-within: a closing dialog hands the focus back to a card button without any keyboard) or, on a touch
 * screen, on a tap (data-open). The panel is always full size and fully formed; while hidden it sits just below the card's edge (the card
 * clips it) and stays in the accessibility tree, so a screen reader always reads it. Its blur and tint are static: an ancestor fading in
 * would isolate backdrop-filter and delay the blur.
 */
const cardSx = (theme: Theme) => ({
  position: 'relative',
  display: 'flex',
  flexDirection: 'column',
  minHeight: 'max(17rem, calc(var(--top-h, 0px) + var(--panel-h, 0px)))',
  borderRadius: '12px',
  boxShadow: 'var(--tk-shadow-3)',
  color: 'white',
  overflow: 'hidden',
  cursor: 'pointer',
  transition: 'transform 0.15s ease, box-shadow 0.15s ease',
  '& .card-top': { position: 'relative' }, // above the cover layers; it stays put while the figures slide in below it
  '& .vehicle-card-link': {
    color: 'inherit',
    textDecoration: 'none',
    '&::after': { content: '""', position: 'absolute', inset: 0 }, // the top block is the link's target; the card's click handler opens the vehicle from everywhere else
    '&:focus-visible': { outline: 'none' },
  },
  // The card's own buttons: above the link's overlay (positioned, later in the tree) and comfortable to tap.
  '& .vehicle-card-action': { position: 'relative', minHeight: 44, minWidth: 44 },
  '&:has(.vehicle-card-link:focus-visible)': { outline: `2px solid ${theme.vars.palette.primary.main}`, outlineOffset: 2 },
  '& .card-panel': { position: 'absolute', inset: 'auto 0 0 0', transform: 'translateY(100%)', transition: 'transform 0.34s cubic-bezier(0.2, 0.8, 0.2, 1)' },
  '@media (hover: hover)': {
    '&:hover': { transform: 'translateY(-2px)', boxShadow: 'var(--tk-shadow-5)' },
    '&:hover .card-panel': { transform: 'translateY(0)' },
  },
  '&:has(:focus-visible)': { transform: 'translateY(-2px)', boxShadow: 'var(--tk-shadow-5)' },
  '&:has(:focus-visible) .card-panel, &[data-open] .card-panel': { transform: 'translateY(0)' },
  // The figures over the picture: a dark scrim drawn as the surfaces are (theme/components.ts SURFACE_TOKENS).
  '& .vehicle-card-stats-container': { background: 'var(--tk-scrim)', backdropFilter: 'var(--tk-scrim-filter)', WebkitBackdropFilter: 'var(--tk-scrim-filter)' },
  '@media (prefers-reduced-motion: reduce)': {
    '&, & .card-panel': { transition: 'none' },
    '&:hover, &:has(:focus-visible)': { transform: 'none' },
  },
})

/**
 * A vehicle on the welcome screen: its picture as the background; the name, the plate, what needs attention and the quick actions at the
 * top, where they stay. The key figures and the spending trend slide up from the bottom edge on hover (pointer devices) or keyboard focus;
 * on a touch screen the first tap shows them and the second tap opens the vehicle (a tap elsewhere hides them again). With a mouse a click
 * anywhere on the card opens the vehicle; keyboard and screen-reader activation always opens it straight away.
 *
 * Quick actions: whoever may add logs gets a Refuel and an Expense button, and a Done button next to every schedule that needs attention;
 * they open the vehicle page's own dialogs right here. A dialog renders outside the card (a portal), yet React bubbles its events through
 * the card, so the click handling only acts on clicks that landed in the card's own DOM (`cardClicks.ts`), and the card's buttons act on the
 * first tap. After an action only this vehicle is asked again (`useCardActions`), so the pages the home page already loaded stay. White
 * text on a dark scrim in either colour scheme, so the card is drawn in the dark one (the `dark` class).
 */
export function VehicleCard({ vehicle: v }: { vehicle: Vehicle }) {
  const { t } = useTranslation()
  const format = useFormat()
  const s = v.summary
  usePendingCount(v.id) // the marks follow the changes waiting
  const none = t('welcome.card.none')
  const dueText = useDueText(v.units.distance)
  // Only what needs attention is on the card (the vehicle's Recurring tab has the rest); overdue first, the server already sorts by urgency.
  const attention = v.recurring.filter((r) => r.status.state !== 'UPCOMING')
  const canLog = canLogFor(v)
  const actions = useCardActions(v.id)
  const touch = useMediaQuery(MEDIA.touch, false)
  const [open, setOpen] = useState(false)
  const card = useRef<HTMLDivElement>(null)
  const navigate = useNavigate()
  const titleLink = useRef<HTMLAnchorElement>(null)
  // One Done dialog for the card, opened from the schedule's own button: a dialog per schedule would leave with its row once done.
  const [done, setDone] = useState<{ item: Vehicle['recurring'][number]; open: boolean } | null>(null)
  // Touch: a tap anywhere else (not in a dialog opened from here) puts the figures away again.
  useEffect(() => {
    if (!open) return
    const away = (e: PointerEvent) => tappedAway(card.current, e.target) && setOpen(false)
    document.addEventListener('pointerdown', away)
    return () => document.removeEventListener('pointerdown', away)
  }, [open])
  // The figures slide up from below the card and must not cover the top block: the card needs both heights to be tall enough for both.
  const top = useRef<HTMLDivElement>(null)
  const panel = useRef<HTMLDivElement>(null)
  const [heights, setHeights] = useState({ top: 0, panel: 0 })
  useLayoutEffect(() => {
    const topElement = top.current
    const panelElement = panel.current
    if (!topElement || !panelElement) return
    const measure = () => {
      const next = { top: topElement.offsetHeight, panel: panelElement.offsetHeight }
      setHeights((current) => (current.top === next.top && current.panel === next.panel ? current : next))
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(topElement)
    observer.observe(panelElement)
    return () => observer.disconnect()
  }, [])

  return (
    <Box
      ref={card}
      className="vehicle-card dark"
      data-open={open ? '' : undefined}
      style={{ '--top-h': `${heights.top}px`, '--panel-h': `${heights.panel}px` } as CSSProperties}
      sx={cardSx}
      // Touch only: the first tap shows the figures instead of opening the vehicle (before the link sees it); the second one opens it.
      // A click without a pointer (detail 0: keyboard, screen reader) goes straight through, and mouse users have hover and focus.
      onClickCapture={(e) => {
        if (!touch || e.detail === 0 || open) return
        const origin = cardClickOrigin(card.current, e.target)
        if (origin === 'outside' || origin === 'button') return // a dialog's own controls; the card's buttons act on the first tap
        e.preventDefault()
        e.stopPropagation()
        setOpen(true)
      }}
      // A click anywhere on the card opens the vehicle (on touch: the second tap). The link's overlay alone is not enough: the chart and
      // other positioned parts sit above it and take the click. Real links and buttons, a dialog's controls, modified clicks (new tab) and
      // selecting text are left alone.
      onClick={(e) => {
        if (e.defaultPrevented || e.detail === 0 || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return
        if (cardClickOrigin(card.current, e.target) !== 'card' || window.getSelection()?.toString()) return
        void navigate(`/vehicles/${v.id}`)
      }}
    >
      <CoverLayers pictureUrl={v.pictureUrl} id={v.id} />
      <Box ref={top} className="card-top" sx={{ p: 1.5 }}>
        <Typography component="h2" variant="h4" sx={{ textShadow: '0 1px 6px rgba(0,0,0,0.6)' }}>
          <Link ref={titleLink} className="vehicle-card-link" to={`/vehicles/${v.id}`} aria-label={t('welcome.card.openAria', { name: v.name })}>
            {v.name}
            <ChevronRight size={20} aria-hidden style={{ verticalAlign: 'text-bottom', marginLeft: 2 }} />
          </Link>
        </Typography>
        <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap', mt: 0.5 }}>
          {v.licensePlate && <Chip variant="solid" color="neutral" label={v.licensePlate} />}
          <Chip color="neutral" label={t(`fuel.${v.fuelType}`)} />
          {!v.canEdit && <Chip variant="solid" color="warning" label={t('welcome.card.logAccess', { level: t(`level.${v.logAccess}`) })} />}
          <PendingBadge entity="vehicles" id={v.id} />
        </Stack>
        {attention.length > 0 && (
          <Stack component="ul" aria-label={t('welcome.card.recurringTitle')} sx={{ gap: 0.5, listStyle: 'none', p: 0, m: 0 }}>
            {attention.slice(0, 3).map((r) => {
              const due = dueText(r.status)
              // Done on this device and not sent yet: marked, and not to be done again meanwhile.
              const doneHere = outbox.markOf('recurring', r.id) === 'done'
              return (
                <li key={r.id}>
                  <Stack direction="row" sx={{ alignItems: 'center', gap: 1, justifyContent: 'space-between' }}>
                    <Stack direction="row" sx={{ alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                      <RecurringStatusBadge state={r.status.state} solid />
                      <Typography variant="caption" sx={{ color: 'white', textShadow: TEXT_SHADOW }}>
                        {due ? `${r.title} · ${due}` : r.title}
                      </Typography>
                    </Stack>
                    {doneHere && <PendingBadge entity="recurring" id={r.id} />}
                    {canLog && !doneHere && (
                      <IconAction
                        className="vehicle-card-action"
                        data-done={r.id}
                        aria-haspopup="dialog"
                        label={t('welcome.card.doneAria', { title: r.title, name: v.name })}
                        onClick={() => setDone({ item: r, open: true })}
                      >
                        <CheckCheck size={16} aria-hidden />
                      </IconAction>
                    )}
                  </Stack>
                </li>
              )
            })}
          </Stack>
        )}
        {done && (
          <RecurringDoneDialog
            vehicle={v}
            items={v.recurring}
            selected={preselect(v.recurring, done.item.id)}
            openedFrom={done.item}
            open={done.open}
            onOpenChange={(next) => setDone((d) => d && { ...d, open: next })}
            onSubmit={actions.done}
            // Done, the schedule leaves this list together with its button (and so may the others done at the same visit): the focus
            // would fall off the page, so it goes to the card's title instead (a cancelled dialog has handed it back to the button).
            onClosed={() => {
              const id = done.item.id
              setDone(null)
              if (!card.current?.querySelector(`[data-done="${id}"]`)) titleLink.current?.focus()
            }}
          />
        )}
        {!v.canEdit && v.owner && (
          <Box sx={{ mt: 0.5 }}>
            <UserChip user={v.owner} />
          </Box>
        )}
        {canLog && (
          <Stack direction="row" sx={{ gap: 1, mt: 1.5, flexWrap: 'wrap' }}>
            <RefuelingFormDialog
              vehicle={v}
              onSubmit={actions.refuel}
              trigger={
                <Button className="vehicle-card-action" aria-label={t('welcome.card.refuelAria', { name: v.name })}>
                  <Fuel size={16} aria-hidden />
                  {t('welcome.card.refuel')}
                </Button>
              }
            />
            <ExpenseFormDialog
              vehicle={v}
              onSubmit={actions.expense}
              trigger={
                <Button variant="soft" color="neutral" className="vehicle-card-action" aria-label={t('welcome.card.expenseAria', { name: v.name })}>
                  <Receipt size={16} aria-hidden />
                  {t('welcome.card.expense')}
                </Button>
              }
            />
          </Stack>
        )}
      </Box>

      <div ref={panel} className="card-panel">
        <Box className="vehicle-card-stats-container" sx={{ display: 'grid' }}>
          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 1, p: 1.5 }}>
            <Figure label={t('welcome.card.odometer')} value={s?.latestOdometer != null ? format.distance(s.latestOdometer, v.units.distance) : none} />
            <Figure label={t('welcome.card.consumption')} value={s?.averageConsumption != null ? format.consumption(s.averageConsumption, v.units) : none} />
            <Figure label={t('welcome.card.lastFillUp')} value={s?.lastFillUpDate ? format.date(s.lastFillUpDate) : t('welcome.card.noFillUps')} />
            <Figure label={t('welcome.card.thisMonth')} value={(s && spentText(s.spending, 'thisMonth', format)) ?? none} />
          </Box>
          {s?.currency && s.fillUpCount + s.expenseCount > 0 && (
            <Box>
              <LazySparkline points={s.spendTrend} currency={s.currency} height={32} />
            </Box>
          )}
        </Box>
        {touch && (
          <Typography variant="caption" component="p" sx={{ textAlign: 'center', opacity: 0.8, pb: 1 }}>
            {t('welcome.card.tapAgain')}
          </Typography>
        )}
      </div>
    </Box>
  )
}

/** One key figure on the card's panel: what it is, then the value. */
function Figure({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <Typography variant="caption" component="p" sx={{ opacity: 0.8 }}>
        {label}
      </Typography>
      <Typography variant="body2" component="p" sx={{ fontWeight: 'fontWeightBold' }}>
        {value}
      </Typography>
    </div>
  )
}
