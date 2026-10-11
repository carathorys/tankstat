import { useEffect, useState } from 'react'
import { deviceData } from './deviceData.ts'
import { foundRows, type FoundRow } from './foundRows.ts'

/** The rows set aside when the server's history changed, read again whenever the device's data changes or an action was taken here. */
export function useFoundRows(): { rows: FoundRow[] | null; vehicleNames: Map<string, string>; refresh: () => void } {
  const [rows, setRows] = useState<FoundRow[] | null>(null)
  const [vehicleNames, setVehicleNames] = useState(new Map<string, string>())
  const [version, setVersion] = useState(0)
  useEffect(() => deviceData.onStoreChange(() => setVersion((v) => v + 1)), [])
  useEffect(() => {
    let live = true
    void (async () => {
      const list = await foundRows.list()
      const names = new Map((await (await deviceData.rows())?.vehicles())?.map((v) => [v.id, v.name]) ?? [])
      for (const row of list) if (row.entity === 'vehicles') names.set(row.id, row.title)
      if (live) {
        setRows(list)
        setVehicleNames(names)
      }
    })()
    return () => {
      live = false
    }
  }, [version])
  return { rows, vehicleNames, refresh: () => setVersion((v) => v + 1) }
}
