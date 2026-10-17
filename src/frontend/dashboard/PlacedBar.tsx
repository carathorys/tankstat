import type { BarProps } from '@mui/x-charts/BarChart'
import { useAnimateBar, useXScale } from '@mui/x-charts/hooks'
import { useContext } from 'react'
import { BarLayoutContext } from './barLayout.ts'

/**
 * A bar of `ChartView`, moved from the place MUI keeps for its group in every period to its place among the groups the period has, centred
 * in the period's band (the x axis' own scale, `useXScale`), at the width MUI gave it. Drawn with MUI's animation (`useAnimateBar`, which
 * honours `skipAnimation`). The stack's top gets rounded corners here (a rounded cap over a square body): MUI's own (`borderRadius`) clip
 * each bar at the place it would have been, so they are off.
 *
 * Only the picture moves: MUI still finds a bar under the pointer at its own place, so the chart keeps to the tooltip by axis (the whole
 * period) and offers no item highlight, item tooltip or item click, which would point at the wrong bar.
 */
export function PlacedBar(props: BarProps) {
  const { ownerState, skipAnimation, seriesId, dataIndex, xOrigin: _xOrigin, yOrigin, layout: orientation, x, y, width, height, ...rest } = props
  const layout = useContext(BarLayoutContext)
  const xScale = useXScale<'band'>()
  const placed = layout?.at(String(seriesId), dataIndex) ?? null
  let left = x
  if (layout && placed) {
    const gap = width * layout.gapRatio
    const centre = (xScale(layout.periodOf(dataIndex)) ?? 0) + xScale.bandwidth() / 2
    left = centre - (placed.of * width + (placed.of - 1) * gap) / 2 + placed.place * (width + gap)
  }
  const r = layout && placed?.top ? Math.max(0, Math.min(layout.radius, width / 2, height / 2)) : 0
  // The body below the rounded cap, and the cap: the top two corners round, the bottom ones stay square on the axis.
  const body = useAnimateBar({ x: left, y: y + r, xOrigin: left, yOrigin, width, height: height - r, skipAnimation, layout: orientation })
  const cap = useAnimateBar({ x: left, y, xOrigin: left, yOrigin, width, height: 2 * r, skipAnimation, layout: orientation })
  const looks = {
    filter: ownerState.isHighlighted ? 'brightness(120%)' : undefined,
    opacity: ownerState.isFaded ? 0.3 : 1,
    'data-highlighted': ownerState.isHighlighted || undefined,
    'data-faded': ownerState.isFaded || undefined,
  }
  if ((layout && !placed) || height <= 0) return null
  // The cap is part of the bar's picture, not a bar of its own (no MUI class: what counts the bars counts the bodies).
  return (
    <>
      <rect {...rest} {...looks} {...body} />
      {r > 0 && <rect {...rest} {...looks} {...cap} className={undefined} rx={r} ry={r} />}
    </>
  )
}
