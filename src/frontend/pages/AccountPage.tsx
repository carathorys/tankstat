import { DataList, Heading, Text } from '@radix-ui/themes'
import { useTranslation } from 'react-i18next'
import type { AuthMode, SessionQuery } from '../gql/generated.ts'
import { ChangePasswordForm } from '../PasswordForms.tsx'

export function AccountPage({ mode, user }: { mode: AuthMode; user: SessionQuery['session']['user'] }) {
  const { t } = useTranslation()

  return (
    <section>
      <Heading mb="3">{t('account.title')}</Heading>
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
      {user && mode === 'STANDALONE' && <ChangePasswordForm />}
      {user && mode !== 'STANDALONE' && <Text as="p">{t('account.managed')}</Text>}
    </section>
  )
}
