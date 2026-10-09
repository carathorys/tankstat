import { useApolloClient, useMutation } from '@apollo/client/react'
import Button from '@mui/material/Button'
import { useTranslation } from 'react-i18next'
import type { ChangeEdit } from '../../dialogs/changeEdit.ts'
import { ExpenseFormDialog, type ExpenseValues } from '../../ExpenseFormDialog.tsx'
import { ResolveSyncChangeDocument, type DistanceUnit, type ParkedChangeFieldsFragment, type VolumeUnit } from '../../gql/generated.ts'
import { keptPhotosOf, type Change } from '../../offline/changes.ts'
import { outbox } from '../../offline/outbox.ts'
import { toChangeInput } from '../../offline/push.ts'
import { uuidV4 } from '../../offline/uuid.ts'
import { RecurringFormDialog, type RecurringValues } from '../../RecurringFormDialog.tsx'
import { RefuelingFormDialog, type RefuelingValues } from '../../RefuelingFormDialog.tsx'
import { VehicleFormDialog, type VehicleValues } from '../../VehicleFormDialog.tsx'

type Values = RefuelingValues | ExpenseValues | VehicleValues | RecurringValues

/**
 * Edit, on Waiting to sync, in the dialog of the entry the change concerns, starting from the values the change carries:
 * - a change waiting on this device stays on it, as an edit that folds into it (an add keeps its id and its photos);
 * - a change the server parked (`parked`) is applied with the edited values, as the person would online, against the version shown of what
 *   is there now (`current`); a refusal stays in the dialog with its reason (and the change stays parked with it).
 */
export function EditChange({
  change,
  units,
  label,
  parked,
  disabled,
  onDone,
}: {
  change: Change
  disabled?: boolean
  units: { distance: DistanceUnit; volume: VolumeUnit }
  /** What the change does and carries, for the button's name. */
  label: string
  parked?: ParkedChangeFieldsFragment
  onDone: (message: string) => void
}) {
  const { t } = useTranslation()
  const client = useApolloClient()
  const [resolve] = useMutation(ResolveSyncChangeDocument)

  const save = async (values: Values) => {
    // The values replace the change's own (the add's id and vehicle stay); photos are never part of an edit.
    const input = { ...change.input, ...values, id: change.targetId, photoIds: undefined }
    if (!parked) {
      await outbox.enqueue({ id: uuidV4(), entity: change.entity, action: 'update', vehicleId: change.vehicleId, targetId: change.targetId, input, expectedVersion: change.expectedVersion ?? null })
      void client.refetchQueries({ include: 'active' }).catch(() => undefined)
      onDone(t('sync.edited'))
      return
    }
    try {
      await resolve({
        // Applied over what the person sees now: changed once more meanwhile, it stays parked (none seen: over whatever is there).
        variables: { input: { id: parked.id, action: 'APPLY', change: toChangeInput({ ...change, input, expectedVersion: parked.current?.version ?? null, base: undefined }) } },
        refetchQueries: ['ParkedChanges'],
        awaitRefetchQueries: true,
      })
    } catch (error) {
      void client.refetchQueries({ include: ['ParkedChanges'] }).catch(() => undefined) // its new reason
      throw error
    }
    void client.refetchQueries({ include: 'active' }).catch(() => undefined)
    onDone(t('sync.appliedNow'))
  }

  const edit: ChangeEdit<never> = {
    initial: change.input as never,
    title: parked ? t('sync.editApplyTitle') : t('sync.editTitle'),
    submitLabel: parked ? t('sync.applyEdited') : t('sync.saveChange'),
    mayWait: !parked && change.action === 'add' && keptPhotosOf(change).length > 0,
  }
  const trigger = (
    <Button variant="soft" size="large" disabled={disabled} aria-label={parked ? t('sync.editApplyAria', { change: label }) : t('sync.editAria', { change: label })}>
      {parked ? t('sync.editApply') : t('sync.edit')}
    </Button>
  )
  const vehicle = { id: change.vehicleId, units }

  switch (change.entity) {
    case 'refuelings':
      return <RefuelingFormDialog trigger={trigger} vehicle={vehicle} change={edit} onSubmit={save} />
    case 'expenses':
      return <ExpenseFormDialog trigger={trigger} vehicle={vehicle} change={edit} onSubmit={save} />
    case 'vehicles':
      return <VehicleFormDialog trigger={trigger} change={edit} onSubmit={save} />
    case 'recurring':
      return <RecurringFormDialog trigger={trigger} vehicleId={change.vehicleId} unit={units.distance} change={edit} onSubmit={save} />
  }
}
