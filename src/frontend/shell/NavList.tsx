import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import { Bell, Car, FileUp, House, LogOut, Settings, ShieldCheck, Trash2 } from 'lucide-react'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { NavLink } from 'react-router'
import { type AuthMode, type SessionQuery } from '../gql/generated.ts'
import { useSignOut } from '../auth/useSignOut.ts'
import { OfflineError } from '../offline/errors.ts'
import { useToast } from '../toast/toastContext.ts'
import { InstallMenuItem } from './InstallMenuItem.tsx'
import { navItemSx } from './navStyles.ts'

/** The navigation links, shared by the docked sidebar and the phone drawer. `onNavigate` lets the drawer close itself. */
export function NavList({ mode, user, onNavigate }: { mode: AuthMode; user: SessionQuery['session']['user']; onNavigate?: () => void }) {
  const { t } = useTranslation()
  const signOut = useSignOut(mode)
  const { toast } = useToast()
  const canSignOut = user !== null && mode !== 'PROXY_HEADER' // behind a proxy the proxy owns the session

  const link = (to: string, label: string, icon: ReactNode) => (
    <Box component={NavLink} to={to} end={to === '/'} sx={navItemSx} onClick={onNavigate}>
      {icon}
      {label}
    </Box>
  )

  return (
    <Stack component="nav" aria-label={t('nav.main')} sx={{ gap: 0.5 }}>
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
          color="neutral"
          sx={(theme) => ({ ...navItemSx(theme), mt: 1.5 })}
          onClick={async () => {
            onNavigate?.()
            try {
              await signOut()
            } catch (error) {
              // Signing out ends the session on the server: without it the device stays signed in, so say why nothing happened.
              if (error instanceof OfflineError) toast(t('errors.offline'))
              else throw error
            }
          }}
        >
          <LogOut size={18} aria-hidden />
          {t('nav.signOut')}
        </Button>
      )}
    </Stack>
  )
}
