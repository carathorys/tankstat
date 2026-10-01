import { useMutation, useQuery } from '@apollo/client/react'
import { Button, Card, Flex, Heading, Text } from '@radix-ui/themes'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { UserChip } from '../../components/UserAvatar.tsx'
import { LabeledSelect } from '../../components/UnitSelect.tsx'
import { LogAccessDocument, SetVehicleLogAccessDocument, type AccessLevel } from '../../gql/generated.ts'
import { ErrorMessage } from '../../messages.tsx'

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
  if (!data) return <Text as="p" role="status">{t('app.loading')}</Text>

  return (
    <Flex direction="column" gap="4">
      <div>
        <Heading as="h2" size="4" mb="1">
          {t('sharing.title')}
        </Heading>
        <Text as="p" size="2" color="gray">
          {t('sharing.description')}
        </Text>
      </div>
      {actionError !== undefined && <ErrorMessage error={actionError} />}

      {data.vehicleLogAccess.length === 0 ? (
        <Text as="p">{t('sharing.none')}</Text>
      ) : (
        <Flex asChild direction="column" gap="2">
          <ul style={{ listStyle: 'none', padding: 0, margin: 0 }}>
            {data.vehicleLogAccess.map((g) => (
              <li key={g.user.id}>
                <Card>
                  <Flex align="center" gap="3" wrap="wrap" justify="between">
                    <UserChip user={g.user} />
                    <Flex align="end" gap="2" wrap="wrap">
                      <LabeledSelect
                        label={t('sharing.changeLevel', { name: g.user.displayName })}
                        value={(g.level === 'DELETE' ? 'DELETE' : 'EDIT') as Shareable}
                        onChange={(l) => void change(g.user.id, l)}
                        options={LEVELS.map((l) => ({ value: l, label: t(`sharing.levels.${l}`) }))}
                      />
                      <Button color="red" variant="soft" size="3" aria-label={t('sharing.revokeAria', { name: g.user.displayName })} onClick={() => void change(g.user.id, 'NONE')}>
                        {t('sharing.revoke')}
                      </Button>
                    </Flex>
                  </Flex>
                </Card>
              </li>
            ))}
          </ul>
        </Flex>
      )}

      <Card>
        <Heading as="h3" size="3" mb="3">
          {t('sharing.addTitle')}
        </Heading>
        {data.shareCandidates.length === 0 ? (
          <Text as="p" size="2" color="gray">
            {t('sharing.noCandidates')}
          </Text>
        ) : (
          <form
            onSubmit={(e) => {
              e.preventDefault()
              if (person) void change(person, level).then(() => setPerson(''))
            }}
          >
            <Flex gap="3" align="end" wrap="wrap">
              <LabeledSelect
                label={t('sharing.person')}
                value={person as string}
                onChange={setPerson}
                options={data.shareCandidates.map((u) => ({ value: u.id, label: u.displayName }))}
                placeholder={t('sharing.choose')}
              />
              <LabeledSelect label={t('sharing.accessLevel')} value={level} onChange={setLevel} options={LEVELS.map((l) => ({ value: l, label: t(`sharing.levels.${l}`) }))} />
              <Button size="3" type="submit" disabled={!person}>
                {t('sharing.add')}
              </Button>
            </Flex>
          </form>
        )}
      </Card>
    </Flex>
  )
}
