import { useMutation } from '@apollo/client/react'
import { AlertDialog, Button, Flex, Heading } from '@radix-ui/themes'
import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import {
  EmptyTrashDocument,
  RestoreVehicleDocument,
  TrashDocument,
  type TrashQuery,
  type TrashQueryVariables,
} from '../gql/generated.ts'
import { DataGrid, type GridColumn } from '../grid/DataGrid.tsx'
import { useFormat } from '../i18n/format.ts'
import { ErrorMessage, SuccessMessage } from '../messages.tsx'

type Row = TrashQuery['trash'][number]

const refetch = { refetchQueries: ['Vehicles', 'Trash'], awaitRefetchQueries: true }

export function TrashPage() {
  const { t } = useTranslation()
  const { dateTime } = useFormat()
  const [restore] = useMutation(RestoreVehicleDocument, refetch)
  const [emptyTrash] = useMutation(EmptyTrashDocument, refetch)
  const [actionError, setActionError] = useState<unknown>()
  const [notice, setNotice] = useState<ReactNode>()
  const none = t('common.none')

  const columns: GridColumn<Row, TrashQueryVariables>[] = [
    { id: 'name', label: 'columns.name', hideable: false, mobile: true, sortField: 'NAME', cell: (r) => r.name },
    { id: 'licensePlate', label: 'columns.licensePlate', include: 'withLicensePlate', sortField: 'LICENSE_PLATE', cell: (r) => r.licensePlate ?? none },
    { id: 'fuelType', label: 'columns.fuelType', include: 'withFuelType', sortField: 'FUEL_TYPE', cell: (r) => (r.fuelType ? t(`fuel.${r.fuelType}`) : none) },
    { id: 'owner', label: 'columns.owner', include: 'withOwner', sortField: 'OWNER', cell: (r) => r.ownerName ?? none },
    { id: 'deletedAt', label: 'columns.deletedAt', include: 'withDeletedAt', mobile: true, sortField: 'DELETED_AT', cell: (r) => (r.deletedAt ? dateTime(r.deletedAt) : none) },
  ]

  async function run(action: () => Promise<unknown>) {
    setActionError(undefined)
    setNotice(undefined)
    try {
      await action()
    } catch (e) {
      setActionError(e)
    }
  }

  return (
    <section>
      <Heading mb="4">{t('trash.title')}</Heading>
      {actionError !== undefined && <ErrorMessage error={actionError} />}
      {notice && <SuccessMessage>{notice}</SuccessMessage>}
      <DataGrid
        gridId="trash"
        query={TrashDocument}
        select={(d) => ({ rows: d.trash, total: d.trashCount })}
        rowKey={(r) => r.id}
        columns={columns}
        defaultSort={{ field: 'DELETED_AT', direction: 'DESC' }}
        emptyText={t('trash.empty')}
        toolbar={({ total }) => (
          <AlertDialog.Root>
            <AlertDialog.Trigger>
              <Button color="red" variant="soft" disabled={total === 0}>
                {t('trash.emptyAction')}
              </Button>
            </AlertDialog.Trigger>
            <AlertDialog.Content maxWidth="450px">
              <AlertDialog.Title>{t('trash.emptyTitle')}</AlertDialog.Title>
              <AlertDialog.Description size="2">{t('trash.emptyDescription', { count: total })}</AlertDialog.Description>
              <Flex gap="3" mt="4" justify="end">
                <AlertDialog.Cancel>
                  <Button variant="soft" color="gray">
                    {t('common.cancel')}
                  </Button>
                </AlertDialog.Cancel>
                <AlertDialog.Action>
                  <Button
                    color="red"
                    onClick={() =>
                      run(async () => {
                        const result = await emptyTrash()
                        setNotice(t('trash.emptied', { count: result.data?.emptyTrash ?? 0 }))
                      })
                    }
                  >
                    {t('trash.emptyAction')}
                  </Button>
                </AlertDialog.Action>
              </Flex>
            </AlertDialog.Content>
          </AlertDialog.Root>
        )}
        actions={(v) => (
          <Button size="1" variant="soft" aria-label={t('trash.restoreAria', { name: v.name })} onClick={() => run(() => restore({ variables: { id: v.id } }))}>
            {t('trash.restore')}
          </Button>
        )}
      />
    </section>
  )
}
