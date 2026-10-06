import { useApolloClient, useMutation } from '@apollo/client/react'
import { Button, Flex } from '@radix-ui/themes'
import { Bell, Car, FileUp, House, LogOut, Settings, ShieldCheck, Trash2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { NavLink } from 'react-router'
import { signedOut } from '../auth/oidc.ts'
import { LogoutDocument, type AuthMode, type SessionQuery } from '../gql/generated.ts'
import { InstallMenuItem } from './InstallMenuItem.tsx'

/** The navigation links, shared by the docked sidebar and the phone drawer. `onNavigate` lets the drawer close itself. */
export function NavList({ mode, user, onNavigate }: { mode: AuthMode; user: SessionQuery['session']['user']; onNavigate?: () => void }) {
  const { t } = useTranslation()
  const client = useApolloClient()
  const [logout] = useMutation(LogoutDocument)
  const canSignOut = user !== null && mode !== 'PROXY_HEADER' // behind a proxy the proxy owns the session

  const link = (to: string, label: string, icon: React.ReactNode) => (
    <NavLink to={to} end={to === '/'} className="nav-link" onClick={onNavigate}>
      {icon}
      {label}
    </NavLink>
  )

  return (
    <Flex asChild direction="column" gap="1">
      <nav aria-label={t('nav.main')}>
        {link('/', t('nav.home'), <House size={18} aria-hidden />)}
        {user?.isAdmin && link('/vehicles', t('nav.vehicles'), <Car size={18} aria-hidden />)}
        {link('/import', t('nav.import'), <FileUp size={18} aria-hidden />)}
        {link('/notifications', t('nav.notifications'), <Bell size={18} aria-hidden />)}
        {link('/trash', t('nav.trash'), <Trash2 size={18} aria-hidden />)}
        {user && link('/account', t('nav.account'), <Settings size={18} aria-hidden />)}
        {user?.isAdmin && link('/admin', t('nav.admin'), <ShieldCheck size={18} aria-hidden />)}
        <InstallMenuItem onNavigate={onNavigate} />
        {canSignOut && (
          <Button
            variant="ghost"
            color="gray"
            size="3"
            style={{ justifyContent: 'flex-start', marginTop: 'var(--space-3)' }}
            onClick={async () => {
              onNavigate?.()
              await logout()
              // Before the store resets: the sign-in screen that follows must wait for a click, not send the browser back to the provider.
              if (mode === 'OIDC') signedOut.mark()
              await client.resetStore()
            }}
          >
            <LogOut size={18} aria-hidden />
            {t('nav.signOut')}
          </Button>
        )}
      </nav>
    </Flex>
  )
}
