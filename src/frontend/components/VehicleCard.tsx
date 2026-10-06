import { Badge, Box, Button, Flex, Grid, Heading, IconButton, Text } from '@radix-ui/themes'
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
import { canLogFor } from '../vehicles.ts'
import { cardClickOrigin, tappedAway } from './cardClicks.ts'
import { CoverLayers } from './CoverLayers.tsx'
import { RecurringStatusBadge } from './RecurringStatus.tsx'
import { useCardActions } from './useCardActions.ts'
import { UserChip } from './UserAvatar.tsx'

type Vehicle = VehicleCardFieldsFragment

/**
 * A vehicle on the welcome screen: its picture as the background; the name, the plate, what needs attention and the quick actions at the
 * top, where they stay. The key figures and the spending trend slide up from the bottom edge on hover (pointer devices) or keyboard focus;
 * on a touch screen the first tap shows them and the second tap opens the vehicle (a tap elsewhere hides them again). With a mouse a click
 * anywhere on the card opens the vehicle; keyboard and screen-reader activation always opens it straight away.
 *
 * Quick actions: whoever may add logs gets a Refuel and an Expense button, and a Done button next to every schedule that needs attention;
 * they open the vehicle page's own dialogs right here. A dialog renders outside the card (a portal), yet React bubbles its events through
 * the card, so the click handling only acts on clicks that landed in the card's own DOM (`cardClicks.ts`), and the card's buttons act on the
 * first tap. After an action only this vehicle is asked again (`useCardActions`), so the pages the home page already loaded stay. The card
 * is never shorter than the top block plus the figures (`--top-h`, `--panel-h`), so revealing them covers nothing, and the cards of a row
 * stretch to the tallest one.
 */
export function VehicleCard({ vehicle: v }: { vehicle: Vehicle }) {
  const { t } = useTranslation()
  const format = useFormat()
  const s = v.summary
  const none = t('welcome.card.none')
  const dueText = useDueText(v.units.distance)
  // Only what needs attention is on the card (the vehicle's Recurring tab has the rest); overdue first, the server already sorts by urgency.
  const attention = v.recurring.filter((r) => r.status.state !== 'UPCOMING')
  const canLog = canLogFor(v)
  const actions = useCardActions(v.id)
  const touch = useMediaQuery('(hover: none)', false)
  const [open, setOpen] = useState(false)
  const card = useRef<HTMLDivElement>(null)
  const navigate = useNavigate()
  const titleLink = useRef<HTMLAnchorElement>(null)
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
      className="vehicle-card"
      data-open={open ? '' : undefined}
      style={{ '--top-h': `${heights.top}px`, '--panel-h': `${heights.panel}px` } as CSSProperties}
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
      <Box ref={top} p="3" className="card-top">
        <Heading as="h2" size="5" style={{ textShadow: '0 1px 6px rgba(0,0,0,0.6)' }}>
          <Link ref={titleLink} className="vehicle-card-link" to={`/vehicles/${v.id}`} aria-label={t('welcome.card.openAria', { name: v.name })}>
            {v.name}
            <ChevronRight size={20} aria-hidden style={{ verticalAlign: 'text-bottom', marginLeft: 2 }} />
          </Link>
        </Heading>
        <Flex gap="2" align="center" wrap="wrap" mt="1">
          {v.licensePlate && (
            <Badge color="gray" variant="solid" highContrast>
              {v.licensePlate}
            </Badge>
          )}
          <Badge color="gray" variant="soft" highContrast>
            {t(`fuel.${v.fuelType}`)}
          </Badge>
          {!v.canEdit && (
            <Badge color="amber" variant="solid">
              {t('welcome.card.logAccess', { level: t(`level.${v.logAccess}`) })}
            </Badge>
          )}
        </Flex>
        {attention.length > 0 && (
          <Flex asChild direction="column" gap="1" mt="2">
            <ul aria-label={t('welcome.card.recurringTitle')} style={{ listStyle: 'none', padding: 0, margin: 0 }}>
              {attention.slice(0, 3).map((r) => {
                const due = dueText(r.status)
                return (
                  <li key={r.id}>
                    <Flex align="center" gap="2" justify="between">
                      <Flex align="center" gap="2" wrap="wrap">
                        <RecurringStatusBadge state={r.status.state} solid />
                        <Text size="1" style={{ color: 'white', textShadow: '0 1px 4px rgba(0,0,0,0.7)' }}>
                          {due ? `${r.title} · ${due}` : r.title}
                        </Text>
                      </Flex>
                      {canLog && (
                        <RecurringDoneDialog
                          vehicle={v}
                          items={v.recurring}
                          selected={preselect(v.recurring, r.id)}
                          openedFrom={r}
                          trigger={
                            <IconButton size="2" variant="soft" highContrast className="vehicle-card-action" data-done={r.id} aria-label={t('welcome.card.doneAria', { title: r.title, name: v.name })}>
                              <CheckCheck size={16} aria-hidden />
                            </IconButton>
                          }
                          onSubmit={actions.done}
                          // Done, the schedule leaves this list together with its button (and so may the others done at the same visit):
                          // the focus would fall off the page, so it goes to the card's title instead (a cancelled dialog finds its button
                          // and hands the focus back to it as usual).
                          onCloseAutoFocus={(e) => {
                            if (card.current?.querySelector(`[data-done="${r.id}"]`)) return
                            e.preventDefault()
                            titleLink.current?.focus()
                          }}
                        />
                      )}
                    </Flex>
                  </li>
                )
              })}
            </ul>
          </Flex>
        )}
        {!v.canEdit && v.owner && (
          <Text as="p" size="1" mt="1" style={{ color: 'white' }}>
            <UserChip user={v.owner} />
          </Text>
        )}
        {canLog && (
          <Flex gap="2" mt="3" wrap="wrap">
            <RefuelingFormDialog
              vehicle={v}
              onSubmit={actions.refuel}
              trigger={
                <Button size="2" variant="solid" className="vehicle-card-action" aria-label={t('welcome.card.refuelAria', { name: v.name })}>
                  <Fuel size={16} aria-hidden />
                  {t('welcome.card.refuel')}
                </Button>
              }
            />
            <ExpenseFormDialog
              vehicle={v}
              onSubmit={actions.expense}
              trigger={
                <Button size="2" variant="soft" highContrast className="vehicle-card-action" aria-label={t('welcome.card.expenseAria', { name: v.name })}>
                  <Receipt size={16} aria-hidden />
                  {t('welcome.card.expense')}
                </Button>
              }
            />
          </Flex>
        )}
      </Box>

      <div ref={panel} className="card-panel">
      <Grid className="vehicle-card-stats-container">
        <Grid className="vehicle-card-stats" columns="2" gap="2" p="3">
          <Box>
            <Text as="p" size="1" style={{ opacity: 0.8 }}>
              {t('welcome.card.odometer')}
            </Text>
            <Text as="p" size="2" weight="bold">
              {s?.latestOdometer != null ? format.distance(s.latestOdometer, v.units.distance) : none}
            </Text>
          </Box>
          <Box>
            <Text as="p" size="1" style={{ opacity: 0.8 }}>
              {t('welcome.card.consumption')}
            </Text>
            <Text as="p" size="2" weight="bold">
              {s?.averageConsumption != null ? format.consumption(s.averageConsumption, v.units) : none}
            </Text>
          </Box>
          <Box>
            <Text as="p" size="1" style={{ opacity: 0.8 }}>
              {t('welcome.card.lastFillUp')}
            </Text>
            <Text as="p" size="2" weight="bold">
              {s?.lastFillUpDate ? format.date(s.lastFillUpDate) : t('welcome.card.noFillUps')}
            </Text>
          </Box>
          <Box>
            <Text as="p" size="1" style={{ opacity: 0.8 }}>
              {t('welcome.card.thisMonth')}
            </Text>
            <Text as="p" size="2" weight="bold">
              {(s && spentText(s.spending, 'thisMonth', format)) ?? none}
            </Text>
          </Box>
        </Grid>
        {s?.currency && s.fillUpCount + s.expenseCount > 0 && (
          <Grid className="vehicle-card-sparkline" columns="1" gap="0" p="0">
            <Box style={{ gridColumn: '1 / -1' }}>
              <LazySparkline points={s.spendTrend} currency={s.currency} height={32} />
            </Box>
          </Grid>
        )}
      </Grid>
      {touch && (
        <Text as="p" size="1" align="center" style={{ opacity: 0.8, paddingBottom: 'var(--space-2)' }}>
          {t('welcome.card.tapAgain')}
        </Text>
      )}
      </div>
    </Box>
  )
}
