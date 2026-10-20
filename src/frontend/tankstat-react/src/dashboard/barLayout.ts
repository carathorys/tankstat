import { createContext } from 'react'
import type { BarPlace } from './chartFormat.ts'

/** How `ChartView` lays out its bars: only the groups (currencies, or series) a period has, side by side and centred in it (#159). */
export interface BarLayout {
  /** The period of a bar (the x axis' value): its band on the axis is where the bar goes. */
  periodOf: (dataIndex: number) => string
  /** The place of a series' bar in a period, null where it has nothing there. */
  at: (seriesId: string, dataIndex: number) => BarPlace | null
  /** The x axis' `barGapRatio`: the gap between two bars of a period, as a part of a bar's width. */
  gapRatio: number
  /** The corner radius of a stack's top. */
  radius: number
}

export const BarLayoutContext = createContext<BarLayout | null>(null)
