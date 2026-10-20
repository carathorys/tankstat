import type { SortDirection } from '../gql/generated.ts'

/** What is remembered about one grid: the same shape in the browser (`tankstat.grid.<id>`) and on the server (`GridSettingsInput`, plus the grid id). */
export interface GridSaved {
  order: string[]
  hidden: string[]
  pageSize: number
  sortColumn: string
  sortDirection: SortDirection
}
