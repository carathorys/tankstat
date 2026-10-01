import { useMutation, useQuery } from '@apollo/client/react'
import { useState } from 'react'
import { ConfirmDialog } from '../ui/Dialog.tsx'
import { VehicleFormDialog } from '../VehicleFormDialog.tsx'
import {
  ADD_VEHICLE_MUTATION,
  DELETE_VEHICLE_MUTATION,
  TRASH_QUERY,
  UPDATE_VEHICLE_MUTATION,
  VEHICLES_QUERY,
  fuelLabel,
  type Vehicle,
} from '../vehicles.ts'

const refetch = { refetchQueries: [VEHICLES_QUERY, TRASH_QUERY], awaitRefetchQueries: true }

export function VehiclesPage() {
  const { data, error, loading } = useQuery(VEHICLES_QUERY)
  const [addVehicle] = useMutation(ADD_VEHICLE_MUTATION, refetch)
  const [updateVehicle] = useMutation(UPDATE_VEHICLE_MUTATION, refetch)
  const [deleteVehicle] = useMutation(DELETE_VEHICLE_MUTATION, refetch)
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<Vehicle>()
  const [deleting, setDeleting] = useState<Vehicle>()
  const [actionError, setActionError] = useState<string>()

  return (
    <section>
      <div className="page-header">
        <h1>Vehicles</h1>
        <button type="button" onClick={() => setAdding(true)}>
          Add vehicle
        </button>
      </div>
      {error && <p role="alert">{error.message}</p>}
      {actionError && <p role="alert">{actionError}</p>}
      {loading && <p>Loading…</p>}
      {data && data.vehicles.length === 0 && <p>No vehicles yet. Add your first vehicle to get started.</p>}
      {data && data.vehicles.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>License plate</th>
              <th>Fuel</th>
              <th>Owner</th>
              <th>Refuelings</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {data.vehicles.map((v) => (
              <tr key={v.id}>
                <td>{v.name}</td>
                <td>{v.licensePlate ?? '–'}</td>
                <td>{fuelLabel(v.fuelType)}</td>
                <td>{v.ownerName ?? '–'}</td>
                <td>{v.refuelings.length}</td>
                <td className="row-actions">
                  {v.canEdit ? (
                    <>
                      <button type="button" aria-label={`Edit ${v.name}`} onClick={() => setEditing(v)}>
                        Edit
                      </button>
                      <button type="button" aria-label={`Delete ${v.name}`} onClick={() => setDeleting(v)}>
                        Delete
                      </button>
                    </>
                  ) : (
                    <span className="muted">view only</span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {adding && (
        <VehicleFormDialog
          open
          onOpenChange={(open) => !open && setAdding(false)}
          onSubmit={(input) => addVehicle({ variables: { input } })}
        />
      )}
      {editing && (
        <VehicleFormDialog
          open
          onOpenChange={(open) => !open && setEditing(undefined)}
          initial={{ name: editing.name, licensePlate: editing.licensePlate, fuelType: editing.fuelType }}
          onSubmit={(input) => updateVehicle({ variables: { input: { ...input, id: editing.id } } })}
        />
      )}
      <ConfirmDialog
        open={deleting !== undefined}
        onOpenChange={(open) => !open && setDeleting(undefined)}
        title={`Move ${deleting?.name ?? 'vehicle'} to the trash?`}
        description="You can restore it from the trash until the trash is emptied."
        confirmLabel="Move to trash"
        onConfirm={async () => {
          setActionError(undefined)
          try {
            if (deleting) await deleteVehicle({ variables: { id: deleting.id } })
          } catch (e) {
            setActionError(e instanceof Error ? e.message : String(e))
          }
        }}
      />
    </section>
  )
}
