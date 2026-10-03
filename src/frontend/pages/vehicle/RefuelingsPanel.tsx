import { useMutation } from '@apollo/client/react'
import { Badge, Button, Flex, IconButton, Text } from '@radix-ui/themes'
import { Pencil, Trash2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { UserChip } from '../../components/UserAvatar.tsx'
import {
  DeleteRefuelingDocument,
  LogRefuelingDocument,
  RefuelingsDocument,
  UpdateRefuelingDocument,
  type DistanceUnit,
  type RefuelingSortField,
  type RefuelingsQuery,
  type RefuelingsQueryVariables,
  type VolumeUnit,
} from '../../gql/generated.ts'
import { DataGrid, type GridColumn } from '../../grid/DataGrid.tsx'
import { useFormat } from '../../i18n/format.ts'
import { ErrorMessage } from '../../messages.tsx'
import { RefuelingFormDialog } from '../../RefuelingFormDialog.tsx'
import { anyAwaiting } from '../../recognition/review.ts'
import { ReviewBadge } from '../../recognition/ReviewState.tsx'

type Row = RefuelingsQuery['refuelings'][number]

const refetch = { refetchQueries: ['Refuelings', 'VehicleDetails', 'LogDefaults'], awaitRefetchQueries: true }

export function RefuelingsPanel({
  vehicle,
  canLog,
}: {
  vehicle: { id: string; units: { distance: DistanceUnit; volume: VolumeUnit } }
  canLog: boolean
}) {
  const { t } = useTranslation()
  const format = useFormat()
  const [logRefueling] = useMutation(LogRefuelingDocument, refetch)
  const [updateRefueling] = useMutation(UpdateRefuelingDocument, refetch)
  const [deleteRefueling] = useMutation(DeleteRefuelingDocument, { ...refetch, refetchQueries: [...refetch.refetchQueries, 'RefuelingTrash'] })
  const [actionError, setActionError] = useState<unknown>()
  const { units } = vehicle

  const columns = useMemo<GridColumn<Row, RefuelingsQueryVariables, RefuelingSortField>[]>(() => {
    const none = t('common.none')
    return [
      {
        id: 'date',
        label: 'columns.date',
        hideable: false,
        mobile: true,
        sortField: 'DATE',
        // Always shown, so it also says when a log waits for its photos or for someone to check what they showed.
        cell: (r) => (
          <Flex direction="column" align="start" gap="1">
            {format.date(r.date)}
            <ReviewBadge state={r.reviewState} />
          </Flex>
        ),
      },
      { id: 'volume', label: 'columns.volume', include: 'withVolume', mobile: true, sortField: 'VOLUME', cell: (r) => (r.volume == null ? none : format.volume(r.volume, units.volume)) },
      { id: 'cost', label: 'columns.cost', include: 'withCost', mobile: true, sortField: 'TOTAL_COST', cell: (r) => (r.totalCost == null || !r.currency ? none : format.money(r.totalCost, r.currency)) },
      {
        id: 'price',
        label: 'columns.price',
        include: 'withPrice',
        sortField: 'PRICE_PER_UNIT',
        cell: (r) => (r.pricePerUnit == null || !r.currency ? none : format.pricePerUnit(r.pricePerUnit, r.currency, units.volume)),
      },
      {
        id: 'consumption',
        label: 'columns.consumption',
        include: 'withConsumption',
        sortField: 'CONSUMPTION',
        cell: (r) => (r.consumption == null ? none : format.consumption(r.consumption, units)),
      },
      { id: 'odometer', label: 'columns.odometer', include: 'withOdometer', sortField: 'ODOMETER', cell: (r) => (r.odometer == null ? none : format.distance(r.odometer, units.distance)) },
      { id: 'fullTank', label: 'columns.fullTank', include: 'withFullTank', cell: (r) => (r.isFullTank == null ? none : r.isFullTank ? t('refuelings.full') : <Badge color="amber">{t('refuelings.partial')}</Badge>) },
      { id: 'note', label: 'columns.note', include: 'withNote', cell: (r) => r.note || none },
      { id: 'createdBy', label: 'columns.createdBy', include: 'withCreatedBy', sortField: 'CREATED_BY', cell: (r) => (r.createdBy ? <UserChip user={r.createdBy} /> : none) },
    ]
  }, [t, format, units])

  async function moveToTrash(row: Row) {
    setActionError(undefined)
    try {
      await deleteRefueling({ variables: { id: row.id } })
    } catch (e) {
      setActionError(e)
    }
  }

  return (
    <>
      {actionError !== undefined && <ErrorMessage error={actionError} />}
      <DataGrid
        gridId="refuelings"
        caption={t('refuelings.title')}
        query={RefuelingsDocument}
        variables={{ vehicleId: vehicle.id }}
        select={(d) => ({ rows: d.refuelings, total: d.refuelingCount })}
        rowKey={(r) => r.id}
        columns={columns}
        defaultSort={{ column: 'date', direction: 'DESC' }}
        emptyText={t('refuelings.empty')}
        pollWhile={anyAwaiting}
        toolbar={({ total }) => (
          <Flex align="center" gap="3" wrap="wrap">
            {canLog && (
              <RefuelingFormDialog
                vehicle={vehicle}
                trigger={<Button size="3">{t('refuelings.add')}</Button>}
                onSubmit={async (input, photoIds) => {
                  const logged = await logRefueling({ variables: { input: { ...input, vehicleId: vehicle.id, photoIds } } })
                  return logged.data ? { id: logged.data.logRefueling.id, photoCount: logged.data.logRefueling.photos.length } : undefined
                }}
              />
            )}
            <Text size="2" color="gray" aria-live="polite">
              {t('refuelings.countLabel', { count: total })}
            </Text>
          </Flex>
        )}
        actions={(r) =>
          r.canEdit ? (
            <Flex gap="2" justify="end">
              <RefuelingFormDialog
                vehicle={vehicle}
                refuelingId={r.id}
                trigger={
                  <IconButton size="3" variant="soft" aria-label={t('refuelings.editAria', { date: format.date(r.date) })}>
                    <Pencil size={16} aria-hidden />
                  </IconButton>
                }
                onSubmit={async (input) => void (await updateRefueling({ variables: { input: { ...input, id: r.id } } }))}
              />
              <ConfirmDialog
                trigger={
                  <IconButton size="3" variant="soft" color="red" aria-label={t('refuelings.deleteAria', { date: format.date(r.date) })}>
                    <Trash2 size={16} aria-hidden />
                  </IconButton>
                }
                title={t('refuelings.trashTitle')}
                description={t('refuelings.trashDescription', { date: format.date(r.date) })}
                confirmLabel={t('refuelings.trashConfirm')}
                onConfirm={() => void moveToTrash(r)}
              />
            </Flex>
          ) : (
            <Text size="2" color="gray">
              {t('refuelings.viewOnly')}
            </Text>
          )
        }
      />
    </>
  )
}
