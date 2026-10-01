import { useMutation, useQuery } from '@apollo/client/react'
import { useState } from 'react'
import { ConfirmDialog } from '../ui/Dialog.tsx'
import { EMPTY_TRASH_MUTATION, RESTORE_VEHICLE_MUTATION, TRASH_QUERY, VEHICLES_QUERY, fuelLabel } from '../vehicles.ts'

const refetch = { refetchQueries: [VEHICLES_QUERY, TRASH_QUERY], awaitRefetchQueries: true }

export function TrashPage() {
  const { data, error, loading } = useQuery(TRASH_QUERY)
  const [restore] = useMutation(RESTORE_VEHICLE_MUTATION, refetch)
  const [emptyTrash] = useMutation(EMPTY_TRASH_MUTATION, refetch)
  const [confirming, setConfirming] = useState(false)
  const [actionError, setActionError] = useState<string>()
  const [notice, setNotice] = useState<string>()
  const count = data?.trash.length ?? 0

  async function run(action: () => Promise<unknown>) {
    setActionError(undefined)
    setNotice(undefined)
    try {
      await action()
    } catch (e) {
      setActionError(e instanceof Error ? e.message : String(e))
    }
  }

  return (
    <section>
      <div className="page-header">
        <h1>Trash</h1>
        <button type="button" disabled={count === 0} onClick={() => setConfirming(true)}>
          Empty trash
        </button>
      </div>
      {error && <p role="alert">{error.message}</p>}
      {actionError && <p role="alert">{actionError}</p>}
      {notice && <p role="status">{notice}</p>}
      {loading && <p>Loading…</p>}
      {data && count === 0 && <p>The trash is empty.</p>}
      {data && count > 0 && (
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>License plate</th>
              <th>Fuel</th>
              <th>Owner</th>
              <th>Deleted</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {data.trash.map((v) => (
              <tr key={v.id}>
                <td>{v.name}</td>
                <td>{v.licensePlate ?? '–'}</td>
                <td>{fuelLabel(v.fuelType)}</td>
                <td>{v.ownerName ?? '–'}</td>
                <td>{new Date(v.deletedAt).toLocaleString()}</td>
                <td className="row-actions">
                  <button type="button" aria-label={`Restore ${v.name}`} onClick={() => run(() => restore({ variables: { id: v.id } }))}>
                    Restore
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        title="Empty the trash?"
        description={`This permanently deletes ${count} ${count === 1 ? 'vehicle' : 'vehicles'} and their refuelings. It cannot be undone.`}
        confirmLabel="Empty trash"
        onConfirm={() =>
          run(async () => {
            const result = await emptyTrash()
            const removed = result.data?.emptyTrash ?? 0
            setNotice(`Permanently deleted ${removed} ${removed === 1 ? 'vehicle' : 'vehicles'}.`)
          })
        }
      />
    </section>
  )
}
