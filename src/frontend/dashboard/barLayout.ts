import { createContext } from 'react'
import type { BarPlace } from './chartFormat.ts'

/** How `ChartView` lays out its bars: only the groups (currencies, or series) a period has, side by side and centred in it (#159). */
export interface BarLayout {
  /** How many groups the chart has, and a series' group among them: MUI lays every group out in every period, in that order. */
  groups: number
  groupOf: (seriesId: string) => number
  /** The place of a series' bar in a period, null where its group has nothing there. */
  at: (seriesId: string, dataIndex: number) => BarPlace | null
  /** The x axis' `barGapRatio`: the gap between two bars of a period, as a part of a bar's width. */
  gapRatio: number
  /** The corner radius of a stack's top. */
  radius: number
}

export const BarLayoutContext = createContext<BarLayout | null>(null)
