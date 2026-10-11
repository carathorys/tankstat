import { useApolloClient } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Card from '@mui/material/Card'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useId } from 'react'
import { useTranslation } from 'react-i18next'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { useFormat } from '../../i18n/format.ts'
import { foundRows, type FoundRow } from '../../offline/foundRows.ts'
import { useFoundRows } from '../../offline/useFoundRows.ts'

/**
 * Found on this device: what this device held of the server's data before a restore took it back, and the server no longer has (or has
 * at an older version). Each row can be sent to the server, as a new entry with its own id and values (or an edit of the older one), or
 * forgotten; nothing goes by itself. Shown only while something is set aside.
 */
export function FoundOnDevice({ onDone }: { onDone: (message: string) => void }) {
  const { t } = useTranslation()
  const format = useFormat()
  const client = useApolloClient()
  const { rows, vehicleNames, refresh } = useFoundRows()
  const id = useId()
  if (!rows?.length) return null

  const groups = new Map<string, FoundRow[]>()
  for (const row of rows) groups.set(row.vehicleId, [...(groups.get(row.vehicleId) ?? []), row])
  const act = async (work: Promise<void>, message: string) => {
    await work
    refresh()
    onDone(message)
  }
  const send = (list: FoundRow[]) => act(foundRows.send(client, list), t('sync.found.sent', { count: list.length }))
  const forget = (list: FoundRow[]) => act(foundRows.forget(list.map((r) => r.id)), t('sync.found.forgotten', { count: list.length }))
  const describe = (row: FoundRow) => {
    const money = (amount: unknown, currency: unknown) => (typeof amount === 'number' && typeof currency === 'string' ? format.money(amount, currency) : null)
    const parts = [
      row.date ? format.date(row.date) : null,
      row.title || null,
      money(row.values.totalCost, row.values.currency) ?? money(row.values.amount, row.values.currency),
    ].filter((p): p is string => !!p)
    const kind: string = t(`sync.found.kinds.${row.entity}` as 'sync.found.kinds.refuelings')
    return parts.length ? `${kind}, ${parts.join(', ')}` : kind
  }

  return (
    <Stack component="section" aria-labelledby={id} sx={{ gap: 1.5, mt: 3 }}>
      <div>
        <Typography id={id} component="h2" variant="h5">
          {t('sync.found.title')}
        </Typography>
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {t('sync.found.hint')}
        </Typography>
      </div>
      {[...groups].map(([vehicleId, list]) => {
        const name = vehicleNames.get(vehicleId) ?? t('sync.unknownVehicle')
        return (
          <Stack key={vehicleId} component="section" aria-label={name} sx={{ gap: 1 }}>
            <Stack direction="row" sx={{ gap: 1.5, alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap' }}>
              <Typography component="h3" variant="h6">
                {name}{' '}
                <Typography component="span" variant="body2" sx={{ color: 'text.secondary' }}>
                  {t('sync.found.count', { count: list.length })}
                </Typography>
              </Typography>
              <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
                <Button variant="soft" size="large" onClick={() => void send(list)} aria-label={t('sync.found.sendAllAria', { vehicle: name })}>
                  {t('sync.found.sendAll')}
                </Button>
                <ConfirmDialog
                  trigger={
                    <Button variant="soft" color="error" size="large" aria-label={t('sync.found.forgetAllAria', { vehicle: name })}>
                      {t('sync.found.forgetAll')}
                    </Button>
                  }
                  title={t('sync.found.forgetTitle')}
                  description={t('sync.found.forgetAllDescription', { count: list.length, vehicle: name })}
                  confirmLabel={t('sync.found.forget')}
                  onConfirm={() => void forget(list)}
                />
              </Stack>
            </Stack>
            <Stack component="ul" sx={{ gap: 1, listStyle: 'none', p: 0, m: 0 }}>
              {list.map((row) => {
                const label = describe(row)
                return (
                  <li key={row.id}>
                    <Card sx={{ p: 1.5 }}>
                      <Stack direction="row" sx={{ gap: 1.5, alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap' }}>
                        <div>
                          <Typography>{label}</Typography>
                          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                            {row.serverVersion === undefined ? t('sync.found.missingOnServer') : t('sync.found.olderOnServer')}
                          </Typography>
                        </div>
                        <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
                          <Button variant="soft" size="large" onClick={() => void send([row])} aria-label={t('sync.found.sendAria', { row: label })}>
                            {t('sync.found.send')}
                          </Button>
                          <ConfirmDialog
                            trigger={
                              <Button variant="soft" color="error" size="large" aria-label={t('sync.found.forgetAria', { row: label })}>
                                {t('sync.found.forget')}
                              </Button>
                            }
                            title={t('sync.found.forgetTitle')}
                            description={t('sync.found.forgetDescription')}
                            confirmLabel={t('sync.found.forget')}
                            onConfirm={() => void forget([row])}
                          />
                        </Stack>
                      </Stack>
                    </Card>
                  </li>
                )
              })}
            </Stack>
          </Stack>
        )
      })}
    </Stack>
  )
}
