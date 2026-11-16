import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import FormControlLabel from '@mui/material/FormControlLabel'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import type { Theme } from '@mui/material/styles'
import Typography from '@mui/material/Typography'
import { ArrowLeftRight, ChevronDown, ZoomIn, ZoomOut } from 'lucide-react'
import { useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { IconAction } from '../../components/IconAction.tsx'
import { Segmented } from '../../components/Segmented.tsx'
import { visuallyHidden } from '../../components/visuallyHidden.ts'
import { todayIso } from '../../forms/dates.ts'
import type { DistanceUnit, TimelineLayer, TyreSeason } from '../../gql/generated.ts'
import { useMediaQuery } from '../../hooks/useMediaQuery.ts'
import { useFormat } from '../../i18n/format.ts'
import { DEFAULT_TIMELINE, TIMELINE_LAYERS, TIMELINE_RANGES, type TimelineChoices } from '../../settings/timelineChoices.ts'
import { useUiSettings } from '../../settings/uiSettingsContext.ts'
import { MEDIA } from '../../theme/media.ts'
import { useToast } from '../../toast/toastContext.ts'
import { isDeviceLine, useEntryWords } from '../entries.ts'
import { InstallationDialog } from '../HistoryDialog.tsx'
import { PastSwapsDialog } from '../PastSwapsDialog.tsx'
import { type Tyres, useTyreWords } from '../tyres.ts'
import { useErrorText } from '../../i18n/errors.ts'
import {
  beforeTracking, boundariesOf, dayOf, draggedDay, gapsOf, isoOf, marksOf, periodsOf, pxPerDay, shownSets, spanOf, ticksOf, ZOOMS,
  type Boundary, type Gap, type Period,
} from './model.ts'
import { FillGapDialog, MoveSwapDialog } from './TimelineDialogs.tsx'
import { useMoveSwap } from './useMoveSwap.ts'

const ROW = 56
const HEAD = 34
/** A drag counts once the pointer moved this far; less is a click. */
const DRAG_THRESHOLD = 4
const TONE: Record<TyreSeason, 'warning' | 'info' | 'success'> = { SUMMER: 'warning', WINTER: 'info', ALL_SEASON: 'success' }

const focusRing = (theme: Theme) => ({ '&:focus-visible': { outline: `2px solid ${theme.vars.palette.primary.main}`, outlineOffset: 2 } })
const gapStripes = (theme: Theme) => `repeating-linear-gradient(90deg, ${theme.vars.palette.error.soft} 0 4px, transparent 4px 8px)`
const hatch = (theme: Theme) => `repeating-linear-gradient(135deg, transparent 0 6px, ${theme.vars.palette.action.hover} 6px 12px)`

/** What the timeline has open: one dialog at a time, opened by a bar, a handle, a gap or a button of the table. */
type Opened = { kind: 'period'; period: Period } | { kind: 'swap'; boundary: Boundary } | { kind: 'gap'; gap: Gap }

/**
 * The timeline of a vehicle's tyre sets (#332): a row per set, time left to right, each time on the car a bar; around them the time
 * before tracking, the gaps nobody recorded, repairs and costs, retired sets. Whoever may log edits from it: a bar opens its period, the ⇄
 * handle between two periods moves that swap (a click, a tap or Enter opens *Move the swap*; with a mouse it can be dragged), a gap asks
 * which set was on the car. Everything is drawn from the tab's `VehicleTyres` answer; the choices (range, layers, hidden sets) are the
 * account's (`UiSettings`), zoom is the screen's. The picture's buttons are the accessible way in, and the same data is a table: shown,
 * or kept for screen readers as text when *Table* is off.
 */
export default function Timeline({
  tyres,
  vehicleId,
  unit,
  canLog,
  pending,
}: {
  tyres: Tyres
  vehicleId: string
  unit: DistanceUnit
  canLog: boolean
  /** The sets a change waiting on this device touched (`usePendingTyres`). */
  pending: ReadonlyMap<string, 'new' | 'changed'>
}) {
  const { t } = useTranslation()
  const format = useFormat()
  const words = useTyreWords(unit)
  const entryWords = useEntryWords()
  const errorText = useErrorText()
  const { toast } = useToast()
  const settings = useUiSettings()
  // Without a provider (the card rendered on its own) the choices live here, at their defaults.
  const [own, setOwn] = useState<TimelineChoices>(DEFAULT_TIMELINE)
  const choices = settings?.timeline ?? own
  const setChoices = settings?.setTimeline ?? setOwn
  const [zoom, setZoom] = useState<number>(1)
  const [menu, setMenu] = useState(false)
  const [opened, setOpened] = useState<{ what: Opened; open: boolean } | null>(null)
  const menuId = useId()
  const phone = useMediaQuery(MEDIA.narrow, false)
  const mouse = !useMediaQuery(MEDIA.touch, false)
  const moveSwap = useMoveSwap(vehicleId)

  const today = dayOf(todayIso())
  const periods = useMemo(() => periodsOf(tyres), [tyres])
  const gaps = useMemo(() => gapsOf(periods), [periods])
  const boundaries = useMemo(() => boundariesOf(periods, tyres.entries), [periods, tyres.entries])
  const sets = shownSets(tyres, choices)
  const shown = new Set(sets.map((s) => s.set.id))
  const layer = (l: TimelineLayer) => choices.layers.includes(l)
  const span = spanOf(choices.range, tyres, periods, today)
  const nameOf = (id: string) => tyres.sets.find((s) => s.set.id === id)?.set.name ?? ''

  // The picture's room: its box less the set column. jsdom lays nothing out (a width of 0), so the minimum scale applies there.
  const scroll = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(0)
  useLayoutEffect(() => {
    const box = scroll.current
    if (!box || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(([entry]) => setWidth(Math.round(entry.contentRect.width)))
    observer.observe(box)
    return () => observer.disconnect()
  }, [])
  const labelWidth = phone ? 96 : 168
  const perDay = pxPerDay(Math.max(0, width - labelWidth - 2), span, zoom)
  const X = (day: number) => (day - span.start) * perDay
  const W = (span.end - span.start) * perDay
  // A phone starts at today (the right end); a zoom keeps the middle of the view where it was.
  const middle = useRef<number | null>(null)
  useLayoutEffect(() => {
    const box = scroll.current
    if (!box) return
    if (middle.current !== null) box.scrollLeft = middle.current * box.scrollWidth - box.clientWidth / 2
    else if (phone) box.scrollLeft = box.scrollWidth
    middle.current = null
  }, [zoom, phone, W])
  const zoomTo = (next: number) => {
    const box = scroll.current
    if (box && box.scrollWidth > 0) middle.current = (box.scrollLeft + box.clientWidth / 2) / box.scrollWidth
    setZoom(next)
  }
  const setRange = (range: TimelineChoices['range']) => {
    setZoom(1) // another range is another picture: back to the whole of it
    setChoices({ ...choices, range })
  }
  const toggleLayer = (l: TimelineLayer, on: boolean) => setChoices({ ...choices, layers: TIMELINE_LAYERS.filter((x) => (x === l ? on : choices.layers.includes(x))) })
  // The hidden sets are one list for all the user's cars: only the id toggled changes, a newly hidden one going last.
  const toggleSet = (id: string, on: boolean) => setChoices({ ...choices, hiddenSets: on ? choices.hiddenSets.filter((x) => x !== id) : [...choices.hiddenSets.filter((x) => x !== id), id] })

  const listed = tyres.sets.filter((s) => layer('RETIRED_SETS') || !s.set.isRetired)
  const hiddenHere = listed.filter((s) => choices.hiddenSets.includes(s.set.id)).length
  const rangeText = t(`tyres.timeline.ranges.${choices.range}`)
  const summary = hiddenHere > 0
    ? t('tyres.timeline.summaryHidden', { range: rangeText, shown: choices.layers.length, count: hiddenHere })
    : t('tyres.timeline.summary', { range: rangeText, count: choices.layers.length })

  const day = (n: number) => format.date(isoOf(n))
  const distanceOf = (p: Period) => (pending.has(p.setId) ? t('tyres.updatedOnSync') : words.distance(p.info.distance.distance, p.info.distance.approximate))
  const periodName = (p: Period, edit: boolean) =>
    t(edit ? 'tyres.timeline.period' : 'tyres.timeline.periodText', { set: nameOf(p.setId), from: day(p.from), until: p.until === null ? t('tyres.timeline.today') : day(p.until), distance: distanceOf(p) })
  const swapName = (b: Boundary) => t('tyres.timeline.swap', { date: day(b.day), off: nameOf(b.before.setId), on: nameOf(b.after.setId) })
  const gapName = (g: Gap, fill: boolean) => t(fill ? 'tyres.timeline.gap' : 'tyres.timeline.gapText', { from: day(g.from), until: day(g.until) })
  const inSpan = (from: number, until: number | null) => (until ?? today) > span.start && from < span.end

  // The swaps with a handle: whoever may log, a line the server knows (one made on this device has no id to move yet), a shown row.
  const handles = canLog ? boundaries.filter((b) => !isDeviceLine(b.line) && (shown.has(b.after.setId) || shown.has(b.before.setId)) && b.day > span.start) : []
  const rowOf = (id: string) => sets.findIndex((s) => s.set.id === id)

  // A drag (a mouse on a desktop only): the handle follows the pointer by whole days; on release the swap is saved, with Undo.
  const [drag, setDrag] = useState<{ id: string; day: number } | null>(null)
  const dragging = useRef<{ boundary: Boundary; startX: number; moved: boolean; day: number } | null>(null)
  const suppressClick = useRef(false)
  const onHandleDown = (e: ReactPointerEvent<HTMLButtonElement>, b: Boundary) => {
    if (!mouse || e.pointerType !== 'mouse' || e.button !== 0) return
    dragging.current = { boundary: b, startX: e.clientX, moved: false, day: b.day }
    e.currentTarget.setPointerCapture?.(e.pointerId)
  }
  const onHandleMove = (e: ReactPointerEvent<HTMLButtonElement>) => {
    const d = dragging.current
    if (!d) return
    const dx = e.clientX - d.startX
    if (!d.moved && Math.abs(dx) < DRAG_THRESHOLD) return
    d.moved = true
    d.day = draggedDay(d.boundary.day, dx, perDay)
    setDrag({ id: d.boundary.line.id, day: d.day })
  }
  const onHandleUp = () => {
    const d = dragging.current
    dragging.current = null
    setDrag(null)
    if (!d?.moved) return
    suppressClick.current = true // the click that follows the release is the drag's, not a request for the dialog
    if (d.day === d.boundary.day) return
    // A refusal puts the handle back (the picture redraws from the answer it has) and says why.
    moveSwap(d.boundary, isoOf(d.day)).catch((err: unknown) => toast(errorText(err)))
  }
  useEffect(() => () => void (dragging.current = null), [])

  const open = (what: Opened) => setOpened({ what, open: true })
  // The dialog closed: a control that is still there has the focus back from the dialog; one that left (a filled gap) hands it to the picture.
  const closed = () => {
    setOpened(null)
    requestAnimationFrame(() => {
      if (!document.activeElement || document.activeElement === document.body) scroll.current?.focus()
    })
  }

  const table = layer('TABLE')
  const rows = tableRows()
  function tableRows() {
    type Row = { key: string; at: number; set: string; from: string; until: string; distance: string; actions?: Opened[]; tone?: 'gap' }
    const out: Row[] = []
    for (const p of periods.filter((x) => shown.has(x.setId) && inSpan(x.from, x.until))) {
      const swap = handles.find((b) => b.after === p)
      out.push({
        key: p.id, at: p.from, set: nameOf(p.setId), from: day(p.from), until: p.until === null ? t('tyres.timeline.onTheCar') : day(p.until), distance: distanceOf(p),
        actions: canLog ? [{ kind: 'period', period: p }, ...(swap ? [{ kind: 'swap' as const, boundary: swap }] : [])] : undefined,
      })
    }
    if (layer('GAPS')) for (const g of gaps.filter((x) => inSpan(x.from, x.until)))
      out.push({ key: `gap:${g.from}`, at: g.from, set: t('tyres.timeline.legend.gap'), from: day(g.from), until: day(g.until), distance: '–', actions: canLog ? [{ kind: 'gap', gap: g }] : undefined, tone: 'gap' })
    for (const s of sets) {
      const before = layer('BEFORE_TRACKING') ? beforeTracking(s, periods) : null
      if (before && inSpan(before.from, before.until)) out.push({ key: `before:${s.set.id}`, at: before.from, set: s.set.name, from: day(before.from), until: day(before.until), distance: `${t('tyres.timeline.table.before')} · ${words.distance(s.set.roughDistance, true)}` })
      if (layer('MARKS')) for (const m of marksOf(tyres.entries, s.set.id).filter((x) => inSpan(dayOf(x.day), dayOf(x.day) + 1)))
        out.push({ key: `mark:${m.id}`, at: dayOf(m.day), set: s.set.name, from: day(dayOf(m.day)), until: '–', distance: entryWords.kind(m.kind) })
      if (s.set.isRetired && s.set.retiredOn) out.push({ key: `retired:${s.set.id}`, at: dayOf(s.set.retiredOn), set: s.set.name, from: day(dayOf(s.set.retiredOn)), until: '–', distance: t('tyres.timeline.retiredOn', { date: day(dayOf(s.set.retiredOn)) }) })
    }
    return out.sort((a, b) => b.at - a.at || (a.key < b.key ? -1 : 1))
  }
  const actionLabel = (o: Opened) => (o.kind === 'period' ? t('tyres.timeline.table.edit') : o.kind === 'swap' ? t('tyres.timeline.table.moveSwap') : t('tyres.timeline.table.fillIn'))
  const actionName = (o: Opened) => (o.kind === 'period' ? periodName(o.period, true) : o.kind === 'swap' ? swapName(o.boundary) : gapName(o.gap, true))

  return (
    <Paper variant="outlined" component="section" aria-labelledby={`${menuId}-title`} data-timeline sx={{ p: 1.75, borderRadius: 3, display: 'flex', flexDirection: 'column', gap: 1.5, minWidth: 0 }}>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', gap: 1 }}>
        <Typography id={`${menuId}-title`} component="h2" variant="h6">
          {t('tyres.timeline.title')}
        </Typography>
        <Stack direction="row" sx={{ alignItems: 'center', gap: 0.5 }}>
          <IconAction label={t('tyres.timeline.zoomOut')} disabled={zoom === ZOOMS[0]} onClick={() => zoomTo(ZOOMS[ZOOMS.indexOf(zoom as (typeof ZOOMS)[number]) - 1])}>
            <ZoomOut size={18} aria-hidden />
          </IconAction>
          <Typography variant="body2" aria-live="polite" sx={{ minWidth: '2.5em', textAlign: 'center', fontVariantNumeric: 'tabular-nums', color: 'text.secondary' }}>
            {zoom}×
          </Typography>
          <IconAction label={t('tyres.timeline.zoomIn')} disabled={zoom === ZOOMS[ZOOMS.length - 1]} onClick={() => zoomTo(ZOOMS[ZOOMS.indexOf(zoom as (typeof ZOOMS)[number]) + 1])}>
            <ZoomIn size={18} aria-hidden />
          </IconAction>
        </Stack>
      </Stack>

      <Stack direction="row" component="ul" aria-hidden sx={{ gap: '6px 14px', flexWrap: 'wrap', listStyle: 'none', p: 0, m: 0, typography: 'caption', color: 'text.secondary' }}>
        {(['SUMMER', 'WINTER', 'ALL_SEASON'] as const).map((s) => (
          <li key={s}>
            <Box component="i" sx={(theme) => ({ display: 'inline-block', width: 22, height: 10, borderRadius: '3px', verticalAlign: '-1px', mr: 0.75, bgcolor: theme.vars.palette[TONE[s]].soft, border: `2px solid ${theme.vars.palette[TONE[s]].main}` })} />
            {t(`tyres.seasons.${s}`)}
          </li>
        ))}
        <li>
          <Box component="i" sx={(theme) => ({ display: 'inline-block', width: 22, height: 10, borderRadius: '3px', verticalAlign: '-1px', mr: 0.75, border: `1px dashed ${theme.vars.palette.text.secondary}`, background: hatch(theme) })} />
          {t('tyres.timeline.legend.before')}
        </li>
        <li>
          <Box component="i" sx={(theme) => ({ display: 'inline-block', width: 22, height: 10, verticalAlign: '-1px', mr: 0.75, background: gapStripes(theme) })} />
          {t('tyres.timeline.legend.gap')}
        </li>
        <li>
          <Box component="i" sx={(theme) => ({ display: 'inline-block', width: 10, height: 10, transform: 'rotate(45deg)', mx: 1, bgcolor: theme.vars.palette.warning.main, border: `1px solid ${theme.vars.palette.text.primary}` })} />
          {t('tyres.timeline.legend.repair')}
        </li>
      </Stack>

      <Stack direction="row" sx={{ alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
        <Button variant="soft" color="neutral" aria-expanded={menu} aria-controls={menuId} endIcon={<ChevronDown size={16} aria-hidden />} onClick={() => setMenu((m) => !m)}>
          {t('tyres.timeline.show')}
        </Button>
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {summary}
        </Typography>
      </Stack>
      <Stack id={menuId} hidden={!menu} sx={{ gap: 1.5, p: 1.5, border: 1, borderColor: 'divider', borderRadius: 3 }}>
        <Stack sx={{ gap: 0.5, alignItems: 'flex-start' }}>
          <Typography variant="body2" component="span" sx={{ fontWeight: 600 }}>
            {t('tyres.timeline.range')}
          </Typography>
          <Segmented label={t('tyres.timeline.range')} value={choices.range} options={TIMELINE_RANGES.map((r) => ({ value: r, label: t(`tyres.timeline.ranges.${r}`) }))} onChange={setRange} />
        </Stack>
        <Box component="fieldset" sx={{ border: 0, p: 0, m: 0, minWidth: 0 }}>
          <Typography component="legend" variant="body2" sx={{ fontWeight: 600, p: 0 }}>
            {t('tyres.timeline.show')}
          </Typography>
          <Stack direction="row" sx={{ flexWrap: 'wrap', columnGap: 1 }}>
            {TIMELINE_LAYERS.map((l) => (
              <FormControlLabel key={l} control={<Checkbox checked={layer(l)} onChange={(e) => toggleLayer(l, e.target.checked)} />} label={t(`tyres.timeline.layers.${l}`)} />
            ))}
          </Stack>
        </Box>
        <Box component="fieldset" sx={{ border: 0, p: 0, m: 0, minWidth: 0 }}>
          <Typography component="legend" variant="body2" sx={{ fontWeight: 600, p: 0 }}>
            {t('tyres.timeline.sets')}
          </Typography>
          <Stack direction="row" sx={{ flexWrap: 'wrap', columnGap: 1 }}>
            {listed.map((s) => (
              <FormControlLabel key={s.set.id} control={<Checkbox checked={!choices.hiddenSets.includes(s.set.id)} onChange={(e) => toggleSet(s.set.id, e.target.checked)} />} label={s.set.name} />
            ))}
          </Stack>
        </Box>
      </Stack>

      <Box
        ref={scroll}
        tabIndex={0}
        role="group"
        aria-label={t('tyres.timeline.picture')}
        sx={(theme) => ({ overflowX: 'auto', border: 1, borderColor: 'divider', borderRadius: 3, bgcolor: 'background.default', ...focusRing(theme) })}
      >
        <Box sx={{ position: 'relative', display: 'grid', gridTemplateColumns: `${labelWidth}px ${W}px`, width: labelWidth + W }}>
          <Box sx={{ position: 'sticky', left: 0, zIndex: 3, bgcolor: 'background.paper', borderRight: 1, borderColor: 'divider', minHeight: HEAD }} />
          <Box aria-hidden sx={{ position: 'relative', minHeight: HEAD, typography: 'caption', color: 'text.secondary' }}>
            {ticksOf(span).map((tick) => (
              <Box key={tick.day} sx={{ position: 'absolute', top: 0, bottom: 0, left: X(tick.day), borderLeft: 1, borderColor: tick.year ? 'text.secondary' : 'divider', px: 0.5, whiteSpace: 'nowrap', fontWeight: tick.year ? 700 : 400, color: tick.year ? 'text.primary' : 'text.secondary', fontSize: '0.7rem' }}>
                {tick.year ? isoOf(tick.day).slice(0, 4) : format.month(tick.month, 'short')}
              </Box>
            ))}
            <Box sx={{ position: 'absolute', top: 16, left: X(today), borderLeft: 2, borderColor: 'primary.main', color: 'primary.main', px: 0.5, fontSize: '0.7rem', whiteSpace: 'nowrap' }}>{t('tyres.timeline.today')}</Box>
          </Box>
          {sets.map((s) => {
            const tone = TONE[s.set.season]
            const before = layer('BEFORE_TRACKING') ? beforeTracking(s, periods) : null
            const waiting = pending.get(s.set.id)
            return [
              <Box key={`${s.set.id}:label`} sx={{ position: 'sticky', left: 0, zIndex: 3, bgcolor: 'background.paper', borderRight: 1, borderTop: 1, borderColor: 'divider', px: 1, py: 0.5, minHeight: ROW, display: 'flex', flexDirection: 'column', justifyContent: 'center', minWidth: 0 }}>
                <Typography variant="body2" sx={{ fontWeight: 700, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={s.set.name}>
                  {phone ? s.set.name.split(' ')[0] : s.set.name}
                </Typography>
                <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                  {t(`tyres.seasons.${s.set.season}`)}
                </Typography>
                {waiting && (
                  <Typography variant="caption" sx={{ color: 'warning.softText', fontWeight: 700 }}>
                    {t(`offline.pending.${waiting}`)}
                  </Typography>
                )}
              </Box>,
              <Box key={`${s.set.id}:track`} sx={{ position: 'relative', minHeight: ROW, borderTop: 1, borderColor: 'divider' }}>
                <Box aria-hidden sx={{ position: 'absolute', top: 0, bottom: 0, left: X(today), borderLeft: 2, borderColor: 'primary.main', opacity: 0.6 }} />
                {before && inSpan(before.from, before.until) && (
                  <Box
                    title={t('tyres.timeline.beforeTip', { date: format.date(s.set.since) })}
                    sx={(theme) => ({ position: 'absolute', top: 16, height: 24, left: X(Math.max(before.from, span.start)), width: X(before.until) - X(Math.max(before.from, span.start)), borderRadius: '6px', border: `1px dashed ${theme.vars.palette.text.secondary}`, background: hatch(theme), typography: 'caption', color: 'text.secondary', display: 'flex', alignItems: 'center', px: 0.75, whiteSpace: 'nowrap', overflow: 'hidden' })}
                  >
                    ≈
                  </Box>
                )}
                {s.set.isRetired && s.set.retiredOn && dayOf(s.set.retiredOn) > span.start && (
                  <Box sx={{ position: 'absolute', top: 8, bottom: 8, left: X(dayOf(s.set.retiredOn)), borderLeft: 3, borderColor: 'text.primary', pl: 0.5, typography: 'caption', color: 'text.secondary', whiteSpace: 'nowrap', display: 'flex', alignItems: 'center' }}>
                    {t('tyres.timeline.retired', { date: format.date(s.set.retiredOn) })}
                  </Box>
                )}
                {periods.filter((p) => p.setId === s.set.id && inSpan(p.from, p.until)).map((p) => {
                  const left = X(Math.max(p.from, span.start))
                  const text = `${day(p.from)} – ${p.until === null ? t('tyres.timeline.onTheCar') : day(p.until)} · ${distanceOf(p)}`
                  const look = (theme: Theme) => ({
                    position: 'absolute' as const, top: 10, height: 36, left, width: Math.max(4, X(p.until ?? today) - left), boxSizing: 'border-box' as const,
                    borderRadius: p.until === null ? '8px 0 0 8px' : '8px', border: `2px ${waiting ? 'dashed' : 'solid'} ${theme.vars.palette[tone].main}`,
                    ...(p.until === null ? { borderRightStyle: 'dashed' as const } : {}),
                    bgcolor: theme.vars.palette[tone].soft, color: theme.vars.palette.text.primary, font: 'inherit', fontSize: '0.75rem', fontWeight: 650,
                    display: 'flex', alignItems: 'center', px: 0.75, whiteSpace: 'nowrap' as const, overflow: 'hidden', textAlign: 'left' as const, m: 0,
                  })
                  return canLog ? (
                    <Box key={p.id} component="button" type="button" data-layout-exempt="timeline" data-period={p.id} aria-label={periodName(p, true)} title={`${s.set.name}: ${text}`} onClick={() => open({ kind: 'period', period: p })} sx={(theme) => ({ ...look(theme), cursor: 'pointer', ...focusRing(theme) })}>
                      {text}
                    </Box>
                  ) : (
                    <Box key={p.id} data-period={p.id} title={`${s.set.name}: ${text}`} sx={look}>
                      {text}
                    </Box>
                  )
                })}
                {layer('MARKS') && marksOf(tyres.entries, s.set.id).filter((m) => inSpan(dayOf(m.day), dayOf(m.day) + 1)).map((m) => (
                  <Box key={m.id} title={t('tyres.timeline.mark', { kind: entryWords.kind(m.kind), date: format.date(m.day) })} sx={(theme) => ({ position: 'absolute', top: 2, left: X(dayOf(m.day)) - 5, width: 10, height: 10, transform: 'rotate(45deg)', bgcolor: theme.vars.palette.warning.main, border: `1px solid ${theme.vars.palette.text.primary}`, zIndex: 1 })} />
                ))}
              </Box>,
            ]
          })}
          {/* Across the rows: the gaps (whole height) and the swaps' handles, each on the row of the set that went on. */}
          <Box sx={{ position: 'absolute', left: labelWidth, top: HEAD, bottom: 0, width: W, pointerEvents: 'none' }}>
            {layer('GAPS') && gaps.filter((g) => inSpan(g.from, g.until)).map((g) => {
              const left = X(Math.max(g.from, span.start))
              const look = (theme: Theme) => ({ position: 'absolute' as const, top: 0, bottom: 0, left, width: Math.max(6, X(g.until) - left), background: gapStripes(theme), border: 0, p: 0, m: 0 })
              return canLog ? (
                <Box key={g.from} component="button" type="button" data-layout-exempt="timeline" data-gap aria-label={gapName(g, true)} title={gapName(g, false)} onClick={() => open({ kind: 'gap', gap: g })} sx={(theme) => ({ ...look(theme), cursor: 'pointer', pointerEvents: 'auto', ...focusRing(theme) })} />
              ) : (
                <Box key={g.from} data-gap title={gapName(g, false)} sx={(theme) => ({ ...look(theme), pointerEvents: 'auto' })} />
              )
            })}
            {handles.map((b) => {
              const row = Math.max(0, rowOf(shown.has(b.after.setId) ? b.after.setId : b.before.setId))
              const at = drag?.id === b.line.id ? drag.day : b.day
              return (
                <Box
                  key={b.line.id}
                  component="button"
                  type="button"
                  data-swap={b.line.id}
                  aria-label={swapName(b)}
                  aria-haspopup="dialog"
                  title={swapName(b)}
                  onPointerDown={(e) => onHandleDown(e, b)}
                  onPointerMove={onHandleMove}
                  onPointerUp={onHandleUp}
                  onPointerCancel={onHandleUp}
                  onClick={() => {
                    if (suppressClick.current) return void (suppressClick.current = false)
                    open({ kind: 'swap', boundary: b })
                  }}
                  sx={(theme) => ({
                    position: 'absolute', top: row * (ROW + 1) + 6, left: X(at) - 22, width: 44, height: 44, border: 0, p: 0, m: 0, background: 'none', pointerEvents: 'auto', zIndex: 2,
                    display: 'flex', alignItems: 'center', justifyContent: 'center', cursor: mouse ? 'ew-resize' : 'pointer', touchAction: 'pan-x', ...focusRing(theme),
                    '@media (prefers-reduced-motion: no-preference)': { transition: drag?.id === b.line.id ? 'none' : 'left 0.15s ease' },
                  })}
                >
                  <Box component="span" sx={(theme) => ({ width: 20, height: 30, borderRadius: '10px', display: 'flex', alignItems: 'center', justifyContent: 'center', border: `2px solid ${theme.vars.palette.primary.main}`, bgcolor: drag?.id === b.line.id ? theme.vars.palette.primary.main : theme.vars.palette.background.paper, color: drag?.id === b.line.id ? theme.vars.palette.primary.contrastText : theme.vars.palette.primary.main })}>
                    <ArrowLeftRight size={12} aria-hidden />
                  </Box>
                </Box>
              )
            })}
            {drag && (
              <Box role="status" sx={(theme) => ({ position: 'absolute', top: -2, left: X(drag.day), transform: 'translateX(-50%)', bgcolor: theme.vars.palette.primary.main, color: theme.vars.palette.primary.contrastText, typography: 'caption', fontWeight: 700, borderRadius: '6px', px: 0.75, whiteSpace: 'nowrap', zIndex: 5 })}>
                {day(drag.day)}
              </Box>
            )}
          </Box>
        </Box>
      </Box>

      {periods.length === 0 && (
        <Stack direction="row" sx={{ gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            {t('tyres.timeline.noSwaps')}
          </Typography>
          {canLog && <PastSwapsDialog vehicleId={vehicleId} unit={unit} tyres={tyres} trigger={<Button variant="soft">{t('tyres.addPast')}</Button>} />}
        </Stack>
      )}

      {/* The same as a table: shown, or kept for screen readers as text (no buttons: the picture's do the work, by their names). */}
      <Box style={table ? undefined : visuallyHidden} sx={{ overflowX: 'auto' }}>
        <Box component="table" sx={{ borderCollapse: 'collapse', width: '100%', typography: 'body2', '& th, & td': { textAlign: 'left', px: 1, py: 0.75, borderTop: 1, borderColor: 'divider', whiteSpace: 'nowrap' }, '& thead th': { borderTop: 0, color: 'text.secondary', fontWeight: 600 } }}>
          <caption style={visuallyHidden}>{t('tyres.timeline.table.caption')}</caption>
          <thead>
            <tr>
              <th scope="col">{t('tyres.timeline.table.set')}</th>
              <th scope="col">{t('tyres.timeline.table.from')}</th>
              <th scope="col">{t('tyres.timeline.table.until')}</th>
              <th scope="col">{t('tyres.timeline.table.distance')}</th>
              {table && canLog && <th scope="col">{t('tyres.timeline.table.actions')}</th>}
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.key}>
                <Box component="th" scope="row" sx={{ fontWeight: r.tone === 'gap' ? 700 : 400, color: r.tone === 'gap' ? 'error.softText' : undefined }}>
                  {r.set}
                </Box>
                <td>{r.from}</td>
                <td>{r.until}</td>
                <td>{r.distance}</td>
                {table && canLog && (
                  <td>
                    <Stack direction="row" sx={{ gap: 0.75 }}>
                      {(r.actions ?? []).map((a) => (
                        <Button key={a.kind} size="small" variant="soft" color="neutral" aria-haspopup="dialog" aria-label={actionName(a)} onClick={() => open(a)} sx={{ minWidth: 44 }}>
                          {actionLabel(a)}
                        </Button>
                      ))}
                    </Stack>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </Box>
      </Box>

      {opened?.what.kind === 'swap' && (
        <MoveSwapDialog vehicleId={vehicleId} tyres={tyres} boundary={opened.what.boundary} open={opened.open} onOpenChange={(next) => setOpened((o) => o && { ...o, open: next })} onClosed={closed} />
      )}
      {opened?.what.kind === 'gap' && (
        <FillGapDialog vehicleId={vehicleId} tyres={tyres} gap={opened.what.gap} open={opened.open} onOpenChange={(next) => setOpened((o) => o && { ...o, open: next })} onClosed={closed} />
      )}
      {opened?.what.kind === 'period' && (
        <InstallationDialog
          vehicleId={vehicleId}
          unit={unit}
          tyres={tyres}
          setId={opened.what.period.setId}
          installation={opened.what.period.info.installation}
          open={opened.open}
          onOpenChange={(next) => setOpened((o) => o && { ...o, open: next })}
          onClosed={closed}
        />
      )}
    </Paper>
  )
}
