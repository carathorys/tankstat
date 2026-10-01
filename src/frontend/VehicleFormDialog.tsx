import { useState, type FormEvent } from 'react'
import { Label } from 'radix-ui'
import { Dialog } from './ui/Dialog.tsx'
import { Select } from './ui/Select.tsx'
import { FUEL_TYPES, type FuelType, type VehicleInput } from './vehicles.ts'

/** Create (no `initial`) or edit (with `initial`) a vehicle. */
export function VehicleFormDialog({
  open,
  onOpenChange,
  initial,
  onSubmit,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  initial?: VehicleInput
  onSubmit: (input: VehicleInput) => Promise<unknown>
}) {
  const [name, setName] = useState(initial?.name ?? '')
  const [plate, setPlate] = useState(initial?.licensePlate ?? '')
  const [fuel, setFuel] = useState<FuelType>(initial?.fuelType ?? 'PETROL')
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const editing = initial !== undefined

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(undefined)
    try {
      await onSubmit({ name, licensePlate: plate.trim() === '' ? null : plate, fuelType: fuel })
      onOpenChange(false)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={editing ? 'Edit vehicle' : 'Add vehicle'}
      description={editing ? 'Change the details of this vehicle.' : 'Enter the details of the new vehicle.'}
    >
      <form onSubmit={submit} className="stack">
        <div>
          <Label.Root htmlFor="vehicle-name">Name</Label.Root>
          <input id="vehicle-name" value={name} onChange={(e) => setName(e.target.value)} autoFocus />
        </div>
        <div>
          <Label.Root htmlFor="vehicle-plate">License plate (optional)</Label.Root>
          <input id="vehicle-plate" value={plate} onChange={(e) => setPlate(e.target.value)} />
        </div>
        <div>
          <span id="vehicle-fuel-label">Fuel</span>{' '}
          <Select label="Fuel" value={fuel} onValueChange={(v) => setFuel(v as FuelType)} options={FUEL_TYPES} />
        </div>
        {error && <p role="alert">{error}</p>}
        <div className="dialog-actions">
          <button type="button" onClick={() => onOpenChange(false)}>
            Cancel
          </button>
          <button type="submit" disabled={busy}>
            {editing ? 'Save changes' : 'Add vehicle'}
          </button>
        </div>
      </form>
    </Dialog>
  )
}
