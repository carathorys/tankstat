import { useMutation, useQuery } from '@apollo/client/react'
import { Button, Dialog, Flex, IconButton, Text } from '@radix-ui/themes'
import { ArrowDown, ArrowUp } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ArrangeVehiclesDocument, SetVehicleOrderDocument, type ArrangeVehiclesQuery } from './gql/generated.ts'
import { ErrorMessage } from './messages.tsx'

type Item = ArrangeVehiclesQuery['myVehicles'][number]

/**
 * The order of the vehicles on the home page: move them up and down, then save. The whole order is sent at once and the server keeps it with
 * the account, so every device shows the same. Opens from its own button (shown from two vehicles on) and asks for the list afresh each time.
 */
export function ArrangeVehiclesDialog() {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const { data, error } = useQuery(ArrangeVehiclesDocument, { skip: !open, fetchPolicy: 'network-only' })
  // The home page is refetched (it redraws anyway); pages loaded with "Show more" start again from the first one.
  const [setOrder] = useMutation(SetVehicleOrderDocument, { refetchQueries: ['Welcome'], awaitRefetchQueries: true })
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<unknown>()

  return (
    <Dialog.Root
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) setSaveError(undefined)
      }}
    >
      <Dialog.Trigger>
        <Button size="3" variant="soft">
          {t('welcome.arrange')}
        </Button>
      </Dialog.Trigger>
      <Dialog.Content maxWidth="450px">
        <Dialog.Title>{t('welcome.arrangeTitle')}</Dialog.Title>
        <Dialog.Description size="2" mb="4">
          {t('welcome.arrangeDescription')}
        </Dialog.Description>
        {error && <ErrorMessage error={error} />}
        {!data && !error && (
          <Text as="p" role="status">
            {t('app.loading')}
          </Text>
        )}
        {data && (
          <ArrangeList
            vehicles={data.myVehicles}
            saving={saving}
            error={saveError}
            onSave={async (ids) => {
              setSaving(true)
              setSaveError(undefined)
              try {
                await setOrder({ variables: { vehicleIds: ids } })
                setOpen(false)
              } catch (e) {
                setSaveError(e)
              } finally {
                setSaving(false)
              }
            }}
          />
        )}
      </Dialog.Content>
    </Dialog.Root>
  )
}

/** The list itself is mounted only with the server's answer, so every opening starts from the order as it is. */
function ArrangeList({ vehicles, saving, error, onSave }: { vehicles: Item[]; saving: boolean; error: unknown; onSave: (ids: string[]) => Promise<void> }) {
  const { t } = useTranslation()
  const [items, setItems] = useState(vehicles)
  const [status, setStatus] = useState('')
  const focusNext = useRef<{ id: string; direction: 'up' | 'down' } | null>(null)
  const list = useRef<HTMLOListElement>(null)

  const move = (index: number, delta: -1 | 1) => {
    const target = index + delta
    if (target < 0 || target >= items.length) return
    const next = [...items]
    next[index] = items[target]
    next[target] = items[index]
    setItems(next)
    setStatus(t('welcome.moved', { name: next[target].name, position: target + 1, total: next.length }))
    focusNext.current = { id: next[target].id, direction: delta === -1 ? 'up' : 'down' }
  }

  // The focus travels with the moved row: it stays on the same button, or goes to the row's other one when this one is now at an end.
  useEffect(() => {
    const pending = focusNext.current
    if (!pending) return
    focusNext.current = null
    const row = list.current?.querySelector<HTMLElement>(`[data-id="${pending.id}"]`)
    const same = row?.querySelector<HTMLButtonElement>(`[data-direction="${pending.direction}"]`)
    const other = row?.querySelector<HTMLButtonElement>(`[data-direction="${pending.direction === 'up' ? 'down' : 'up'}"]`)
    ;(same && !same.disabled ? same : other)?.focus()
  }, [items])

  return (
    <>
      <Flex asChild direction="column" gap="2">
        <ol ref={list} aria-label={t('welcome.arrangeList')} style={{ listStyle: 'none', padding: 0, margin: 0 }}>
          {items.map((v, i) => (
            <li key={v.id} data-id={v.id}>
              <Flex align="center" gap="3">
                <Text size="2" color="gray" style={{ minWidth: '1.5rem' }} aria-hidden>
                  {i + 1}.
                </Text>
                <Flex direction="column" style={{ flexGrow: 1, minWidth: 0 }}>
                  <Text size="3" weight="medium">
                    {v.name}
                  </Text>
                  {v.licensePlate && (
                    <Text size="1" color="gray">
                      {v.licensePlate}
                    </Text>
                  )}
                </Flex>
                <IconButton size="3" variant="soft" color="gray" style={{ width: 44, height: 44 }} data-direction="up" disabled={i === 0 || saving} aria-label={t('welcome.moveUp', { name: v.name })} onClick={() => move(i, -1)}>
                  <ArrowUp size={18} aria-hidden />
                </IconButton>
                <IconButton size="3" variant="soft" color="gray" style={{ width: 44, height: 44 }} data-direction="down" disabled={i === items.length - 1 || saving} aria-label={t('welcome.moveDown', { name: v.name })} onClick={() => move(i, 1)}>
                  <ArrowDown size={18} aria-hidden />
                </IconButton>
              </Flex>
            </li>
          ))}
        </ol>
      </Flex>
      <Text as="p" size="2" color="gray" role="status" mt="3" style={{ minHeight: '1.5em' }}>
        {status}
      </Text>
      {error !== undefined && <ErrorMessage error={error} />}
      <Flex gap="3" justify="end" mt="4">
        <Dialog.Close>
          <Button variant="soft" color="gray" size="3" disabled={saving}>
            {t('common.cancel')}
          </Button>
        </Dialog.Close>
        <Button size="3" disabled={saving} onClick={() => void onSave(items.map((v) => v.id))}>
          {t('welcome.arrangeSave')}
        </Button>
      </Flex>
    </>
  )
}
