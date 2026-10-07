import { useMutation, useQuery } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { ArrowDown, ArrowUp } from 'lucide-react'
import { motion } from 'motion/react'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { IconAction } from './components/IconAction.tsx'
import { Loading } from './components/Loading.tsx'
import { QUICK } from './components/motion.ts'
import { DialogButtons, DialogCancel, DialogFrame } from './dialogs/DialogFrame.tsx'
import { DialogTrigger } from './dialogs/DialogTrigger.tsx'
import { useDialogState } from './dialogs/useDialogState.ts'
import { ArrangeVehiclesDocument, SetVehicleOrderDocument, type ArrangeVehiclesQuery } from './gql/generated.ts'
import { ErrorMessage } from './messages.tsx'
import { useToast } from './toast/toastContext.ts'

type Item = ArrangeVehiclesQuery['myVehicles'][number]

/**
 * The order of the vehicles on the home page: move them up and down, then save. The whole order is sent at once and the server keeps it with
 * the account, so every device shows the same. Opens from its own button (shown from two vehicles on) and asks for the list afresh each time
 * (at most the first 200 vehicles, the server's page limit).
 */
export function ArrangeVehiclesDialog() {
  const { t } = useTranslation()
  const { toast } = useToast()
  const [open, setOpen] = useDialogState()
  const { data, error, loading } = useQuery(ArrangeVehiclesDocument, { skip: !open, fetchPolicy: 'network-only' })
  // The home page is refetched (it redraws anyway); pages loaded with "Show more" start again from the first one.
  const [setOrder] = useMutation(SetVehicleOrderDocument, { refetchQueries: ['Welcome'], awaitRefetchQueries: true })
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<unknown>()

  return (
    <>
      <DialogTrigger
        trigger={
          <Button size="large" variant="soft">
            {t('welcome.arrange')}
          </Button>
        }
        open={open}
        onOpen={() => {
          setSaveError(undefined) // what went wrong the last time is not this time's
          setOpen(true)
        }}
      />
      {/* busy: closing while it is being saved would lose track of it (and show its error on the next opening). */}
      <DialogFrame open={open} onClose={() => setOpen(false)} busy={saving} title={t('welcome.arrangeTitle')} description={t('welcome.arrangeDescription')}>
        {error && <ErrorMessage error={error} />}
        {!error && (loading || !data) && <Loading />}
        {data && !loading && (
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
                toast(t('toast.saved'))
              } catch (e) {
                setSaveError(e)
              } finally {
                setSaving(false)
              }
            }}
          />
        )}
      </DialogFrame>
    </>
  )
}

/** The list is mounted only with this opening's answer (the last one is still around while it loads), so it starts from the order as it is. */
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

  const arrow = { width: 44, height: 44 }
  return (
    <>
      <Stack component="ol" ref={list} aria-label={t('welcome.arrangeList')} sx={{ gap: 1, listStyle: 'none', p: 0, m: 0 }}>
        {items.map((v, i) => (
          <motion.li key={v.id} data-id={v.id} layout transition={QUICK}>
            <Stack direction="row" sx={{ alignItems: 'center', gap: 1.5 }}>
              <Typography variant="body2" aria-hidden sx={{ color: 'text.secondary', minWidth: '1.5rem' }}>
                {i + 1}.
              </Typography>
              <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                <Typography sx={{ fontWeight: 'fontWeightMedium' }}>{v.name}</Typography>
                {v.licensePlate && (
                  <Typography variant="caption" component="p" sx={{ color: 'text.secondary' }}>
                    {v.licensePlate}
                  </Typography>
                )}
              </Box>
              <IconAction size="large" sx={arrow} data-direction="up" disabled={i === 0 || saving} label={t('welcome.moveUp', { name: v.name })} onClick={() => move(i, -1)}>
                <ArrowUp size={18} aria-hidden />
              </IconAction>
              <IconAction size="large" sx={arrow} data-direction="down" disabled={i === items.length - 1 || saving} label={t('welcome.moveDown', { name: v.name })} onClick={() => move(i, 1)}>
                <ArrowDown size={18} aria-hidden />
              </IconAction>
            </Stack>
          </motion.li>
        ))}
      </Stack>
      <Typography variant="body2" role="status" sx={{ color: 'text.secondary', mt: 1.5, minHeight: '1.5em' }}>
        {status}
      </Typography>
      {error !== undefined && <ErrorMessage error={error} />}
      <DialogButtons>
        <DialogCancel disabled={saving} />
        <Button disabled={saving} onClick={() => void onSave(items.map((v) => v.id))}>
          {t('welcome.arrangeSave')}
        </Button>
      </DialogButtons>
    </>
  )
}
