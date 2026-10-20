import { useEffect, useState } from 'react'
import { deviceData } from './deviceData.ts'

/**
 * How many rows set aside when the server's history changed are listed under *Found on this device* (those a download judged), for the
 * navigation: without an entry there the person would never come across them. Light enough for the shell: the store's rows, nothing more.
 */
export function useFoundCount(): number {
  const [count, setCount] = useState(0)
  const [version, setVersion] = useState(0)
  useEffect(() => {
    const again = () => setVersion((v) => v + 1)
    const offStore = deviceData.onStoreChange(again)
    const offFound = deviceData.onFoundChange(again)
    return () => {
      offStore()
      offFound()
    }
  }, [])
  useEffect(() => {
    let live = true
    void (async () => {
      const rows = await (await deviceData.rows())?.found().catch(() => [])
      if (live) setCount(rows?.filter((row) => row.checked).length ?? 0)
    })()
    return () => {
      live = false
    }
  }, [version])
  return count
}
