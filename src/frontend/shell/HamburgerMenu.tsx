import { useApolloClient, useMutation } from '@apollo/client/react'
import { DropdownMenu, IconButton } from '@radix-ui/themes'
import { Menu } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { LogoutDocument, type AuthMode, type SessionQuery } from '../gql/generated.ts'

export function HamburgerMenu({ mode, user }: { mode: AuthMode; user: SessionQuery['session']['user'] }) {
  const { t } = useTranslation()
  const client = useApolloClient()
  const [logout] = useMutation(LogoutDocument)
  const canSignOut = user !== null && mode !== 'PROXY_HEADER' // behind a proxy the proxy owns the session

  return (
    <DropdownMenu.Root>
      <DropdownMenu.Trigger>
        <IconButton variant="soft" color="gray" highContrast aria-label={t('nav.menu')}>
          <Menu size={18} />
        </IconButton>
      </DropdownMenu.Trigger>
      <DropdownMenu.Content align="start">
        <DropdownMenu.Item asChild>
          <Link to="/vehicles">{t('nav.vehicles')}</Link>
        </DropdownMenu.Item>
        <DropdownMenu.Item asChild>
          <Link to="/trash">{t('nav.trash')}</Link>
        </DropdownMenu.Item>
        {user && (
          <DropdownMenu.Item asChild>
            <Link to="/account">{t('nav.account')}</Link>
          </DropdownMenu.Item>
        )}
        {user?.isAdmin && (
          <DropdownMenu.Item asChild>
            <Link to="/admin">{t('nav.admin')}</Link>
          </DropdownMenu.Item>
        )}
        {canSignOut && (
          <>
            <DropdownMenu.Separator />
            <DropdownMenu.Item
              onSelect={async () => {
                await logout()
                await client.resetStore()
              }}
            >
              {t('nav.signOut')}
            </DropdownMenu.Item>
          </>
        )}
      </DropdownMenu.Content>
    </DropdownMenu.Root>
  )
}
