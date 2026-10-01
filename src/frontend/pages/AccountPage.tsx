import { useApolloClient } from '@apollo/client/react'
import { Avatar, DataList, Heading, Text } from '@radix-ui/themes'
import { useTranslation } from 'react-i18next'
import type { AuthMode, SessionQuery } from '../gql/generated.ts'
import { ImagePicker } from '../components/ImagePicker.tsx'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { avatarPath } from '../pictures/upload.ts'
import { ChangePasswordForm } from '../PasswordForms.tsx'

export function AccountPage({ mode, user }: { mode: AuthMode; user: SessionQuery['session']['user'] }) {
  const { t } = useTranslation()
  const client = useApolloClient()
  usePageTitle(t('account.title'))

  return (
    <section aria-labelledby="page-title">
      <Heading id="page-title" mb="3">{t('account.title')}</Heading>
      {user ? (
        <DataList.Root>
          <DataList.Item>
            <DataList.Label>{t('account.name')}</DataList.Label>
            <DataList.Value>{user.displayName}</DataList.Value>
          </DataList.Item>
          <DataList.Item>
            <DataList.Label>{t('account.email')}</DataList.Label>
            <DataList.Value>{user.email || t('common.none')}</DataList.Value>
          </DataList.Item>
          <DataList.Item>
            <DataList.Label>{t('account.role')}</DataList.Label>
            <DataList.Value>{user.isAdmin ? t('role.admin') : t('role.user')}</DataList.Value>
          </DataList.Item>
        </DataList.Root>
      ) : (
        <Text as="p">{t('account.noAuth')}</Text>
      )}
      {user && mode !== 'NONE' && (
        <section aria-labelledby="avatar-heading">
          <Heading as="h2" size="4" id="avatar-heading" my="4">
            {t('account.avatar')}
          </Heading>
          <ImagePicker
            preview={<Avatar size="7" radius="full" src={user.avatarUrl ?? undefined} fallback={user.displayName.slice(0, 1).toUpperCase()} alt={t('image.avatarFor', { name: user.displayName })} />}
            hasImage={Boolean(user.avatarUrl)}
            path={avatarPath}
            maxEdge={384}
            square
            onChanged={() => client.refetchQueries({ include: ['Session', 'Admin', 'Vehicles', 'Refuelings', 'Trash', 'RefuelingTrash', 'LogAccess'] })}
          />
          <Text as="p" size="1" color="gray" mt="2">
            {t('account.avatarHelp')}
          </Text>
        </section>
      )}
      {user && mode === 'STANDALONE' && <ChangePasswordForm />}
      {user && mode !== 'STANDALONE' && <Text as="p">{t('account.managed')}</Text>}
    </section>
  )
}
