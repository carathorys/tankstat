import { useQuery } from '@apollo/client/react'
import { useEffect, useRef, useState } from 'react'
import { VehicleTyresDocument, type DistanceUnit } from '../gql/generated.ts'
import { SwapDialog } from './SwapDialog.tsx'
import { useReminderDismiss } from './useReminderDismiss.ts'

/** A home card's swap reminder acted on: the set, the season it is due for, and which of the row's two buttons was tapped. */
export interface CardReminderAction {
  setId: string
  season: string
  kind: 'swap' | 'dismiss'
}

/**
 * What a home card's swap row does once one of its buttons is tapped (#292), in a chunk of its own: the card is on the home page, the
 * tyre code and the vehicle's tyres are not, so both load only now. *Swap* asks for the vehicle's tyres (the device's kept answer while
 * out of reach) and opens the swap dialog with the set chosen; *Not this season* dismisses it, with the toast's Undo. Either way the card
 * is asked again afterwards (`refresh`), since a tyre change asks only for the Tyres tab's answer, and `onDone` says it is over (with
 * what went wrong, if anything).
 */
export function CardReminder({
  vehicleId,
  unit,
  action,
  refresh,
  onDone,
}: {
  vehicleId: string
  unit: DistanceUnit
  action: CardReminderAction
  refresh: () => Promise<unknown>
  onDone: (error?: unknown) => void
}) {
  // The vehicle's tyres first, for either button: the swap dialog shows them, and a dismissal kept on this device is checked against them.
  const { data, error } = useQuery(VehicleTyresDocument, { variables: { vehicleId }, fetchPolicy: 'cache-and-network' })
  const dismiss = useReminderDismiss(vehicleId, refresh)
  const [open, setOpen] = useState(true)
  const tyres = data?.vehicleTyres
  const failed = !tyres && error
  const started = useRef(false)
  useEffect(() => {
    if (started.current) return
    if (failed) {
      started.current = true
      onDone(failed)
    } else if (tyres && action.kind === 'dismiss') {
      started.current = true // once, also where effects run twice
      dismiss(action.setId, action.season).then(() => onDone(), onDone)
    }
  }, [action, dismiss, failed, onDone, tyres])
  if (!tyres || action.kind !== 'swap') return null
  return (
    <SwapDialog
      vehicleId={vehicleId}
      unit={unit}
      tyres={tyres}
      setId={action.setId}
      open={open}
      onOpenChange={setOpen}
      onClosed={() => void refresh().then(() => onDone())}
    />
  )
}
