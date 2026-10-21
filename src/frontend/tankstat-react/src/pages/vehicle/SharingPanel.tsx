import { useMutation, useQuery } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Card from '@mui/material/Card'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { UserChip } from '../../components/UserAvatar.tsx'
import { LabeledSelect } from '../../components/UnitSelect.tsx'
import { LogAccessDocument, SetVehicleLogAccessDocument, type AccessLevel } from '../../gql/generated.ts'
import { ErrorMessage } from '../../messages.tsx'
import { Loading } from '../../components/Loading.tsx'

type Shareable = 'EDIT' | 'DELETE'
const LEVELS: Shareable[] = ['EDIT', 'DELETE']
const refetch = { refetchQueries: ['LogAccess'], awaitRefetchQueries: true }

/** Who may work with this vehicle's logs through a grant on this one vehicle (the vehicle itself stays untouched). */
export function SharingPanel({ vehicleId }: { vehicleId: string }) {
  const { t } = useTranslation()
  const { data, error } = useQuery(LogAccessDocument, { variables: { vehicleId }, fetchPolicy: 'cache-and-network' })
  const [setAccess] = useMutation(SetVehicleLogAccessDocument, refetch)
  const [actionError, setActionError] = useState<unknown>()
  const [person, setPerson] = useState('')
  const [level, setLevel] = useState<Shareable>('EDIT')

  async function change(userId: string, next: AccessLevel) {
    setActionError(undefined)
    try {
      await setAccess({ variables: { input: { vehicleId, userId, level: next } } })
    } catch (e) {
      setActionError(e)
    }
  }

  if (error) return <ErrorMessage error={error} />
  if (!data) return <Loading />

  return (
    <Stack sx={{ gap: 2 }}>
      <div>
        <Typography component="h2" variant="h5" sx={{ mb: 0.5 }}>
          {t('sharing.title')}
        </Typography>
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {t('sharing.description')}
        </Typography>
      </div>
      {actionError !== undefined && <ErrorMessage error={actionError} />}

      {data.vehicleLogAccess.length === 0 ? (
        <Typography>{t('sharing.none')}</Typography>
      ) : (
        <Stack component="ul" sx={{ gap: 1, listStyle: 'none', p: 0, m: 0 }}>
          {data.vehicleLogAccess.map((g) => (
            <li key={g.user.id}>
              <Card sx={{ p: 1.5 }}>
                <Stack direction="row" sx={{ alignItems: 'center', gap: 1.5, flexWrap: 'wrap', justifyContent: 'space-between' }}>
                  <UserChip user={g.user} />
                  <Stack direction="row" sx={{ alignItems: 'flex-end', gap: 1, flexWrap: 'wrap' }}>
                    <LabeledSelect
                      label={t('sharing.changeLevel', { name: g.user.displayName })}
                      value={(g.level === 'DELETE' ? 'DELETE' : 'EDIT') as Shareable}
                      onChange={(l) => void change(g.user.id, l)}
                      options={LEVELS.map((l) => ({ value: l, label: t(`sharing.levels.${l}`) }))}
                    />
                    <Button color="error" variant="soft" size="large" aria-label={t('sharing.revokeAria', { name: g.user.displayName })} onClick={() => void change(g.user.id, 'NONE')}>
                      {t('sharing.revoke')}
                    </Button>
                  </Stack>
                </Stack>
              </Card>
            </li>
          ))}
        </Stack>
      )}

      <Card sx={{ p: 1.5 }}>
        <Typography component="h3" variant="h6" sx={{ mb: 1.5 }}>
          {t('sharing.addTitle')}
        </Typography>
        {data.shareCandidates.length === 0 ? (
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            {t('sharing.noCandidates')}
          </Typography>
        ) : (
          <form
            onSubmit={(e) => {
              e.preventDefault()
              if (person) void change(person, level).then(() => setPerson(''))
            }}
          >
            <Stack direction="row" sx={{ gap: 1.5, alignItems: 'flex-end', flexWrap: 'wrap' }}>
              <LabeledSelect
                label={t('sharing.person')}
                value={person as string}
                onChange={setPerson}
                options={data.shareCandidates.map((u) => ({ value: u.id, label: u.displayName }))}
                placeholder={t('sharing.choose')}
              />
              <LabeledSelect label={t('sharing.accessLevel')} value={level} onChange={setLevel} options={LEVELS.map((l) => ({ value: l, label: t(`sharing.levels.${l}`) }))} />
              <Button size="large" type="submit" disabled={!person}>
                {t('sharing.add')}
              </Button>
            </Stack>
          </form>
        )}
      </Card>
    </Stack>
  )
}
