import type { BarProps } from '@mui/x-charts/BarChart'
import { useContext } from 'react'
import { BarLayoutContext } from './barLayout.ts'

/**
 * A bar of `ChartView`, moved from the place MUI keeps for its group in every period to its place among the groups the period has. MUI
 * fills a period's band with every group, `x = start + group × (width + gap)`, so the band's centre is known from the bar itself. The
 * stack's top gets rounded corners here: MUI's own (`borderRadius`) clip each bar at the place it would have been, so they are off.
 */
export function PlacedBar(props: BarProps) {
  const { ownerState, skipAnimation: _skipAnimation, seriesId, dataIndex, xOrigin: _xOrigin, yOrigin: _yOrigin, layout: _orientation, x, y, width, height, ...rest } = props
  const layout = useContext(BarLayoutContext)
  const looks = {
    filter: ownerState.isHighlighted ? 'brightness(120%)' : undefined,
    opacity: ownerState.isFaded ? 0.3 : 1,
    'data-highlighted': ownerState.isHighlighted || undefined,
    'data-faded': ownerState.isFaded || undefined,
  }
  if (!layout) return <rect {...rest} {...looks} x={x} y={y} width={width} height={height} />

  const placed = layout.at(String(seriesId), dataIndex)
  if (!placed || height <= 0) return null
  const gap = width * layout.gapRatio
  const step = width + gap
  const centre = x - layout.groupOf(String(seriesId)) * step + (layout.groups * width + (layout.groups - 1) * gap) / 2
  const left = centre - (placed.of * width + (placed.of - 1) * gap) / 2 + placed.place * step
  if (!placed.top) return <rect {...rest} {...looks} x={left} y={y} width={width} height={height} />
  const r = Math.min(layout.radius, width / 2, height)
  const right = left + width
  const bottom = y + height
  return <path {...rest} {...looks} d={`M${left},${bottom}V${y + r}Q${left},${y} ${left + r},${y}H${right - r}Q${right},${y} ${right},${y + r}V${bottom}Z`} />
}
