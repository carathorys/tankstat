import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import Chip from '@mui/material/Chip'
import Typography from '@mui/material/Typography'
import { Bell, Car, CloudUpload, DatabaseBackup, FileUp, House, LogOut, Settings, ShieldCheck, SlidersHorizontal, Trash2, Users } from 'lucide-react'
import { useId, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { NavLink } from 'react-router'
import { type AuthMode, type SessionQuery } from '../gql/generated.ts'
import { SignOutConfirm } from '../auth/SignOutConfirm.tsx'
import { useSignOutFlow } from '../auth/useSignOut.ts'
import { usePendingCount } from '../offline/outbox.ts'
import { useFoundCount } from '../offline/useFoundCount.ts'
import { useParkedChanges } from '../offline/useParkedChanges.ts'
import { InstallMenuItem } from './InstallMenuItem.tsx'
import { navItemSx } from './navStyles.ts'

/** The navigation links, shared by the docked sidebar and the phone drawer. `onNavigate` lets the drawer close itself. */
export function NavList({ mode, user, onNavigate }: { mode: AuthMode; user: SessionQuery['session']['user']; onNavigate?: () => void }) {
  const { t } = useTranslation()
  // Signing out works with the server out of reach too, and asks first when it would delete changes that only this device holds (#162).
  const signOut = useSignOutFlow(mode, onNavigate)
  const canSignOut = user !== null && mode !== 'PROXY_HEADER' // behind a proxy the proxy owns the session
  const waiting = usePendingCount()
  const parked = useParkedChanges().parked?.length ?? 0
  const found = useFoundCount()
  const adminHeading = useId()

  const link = (to: string, label: string, icon: ReactNode) => (
    <Box component={NavLink} to={to} end={to === '/' || to === '/admin'} sx={navItemSx} onClick={onNavigate}>
      {icon}
      {label}
    </Box>
  )

  return (
    <Stack component="nav" aria-label={t('nav.main')} sx={{ gap: 0.5 }}>
      {link('/', t('nav.home'), <House size={18} aria-hidden />)}
      {link('/import', t('nav.import'), <FileUp size={18} aria-hidden />)}
      {link('/notifications', t('nav.notifications'), <Bell size={18} aria-hidden />)}
      {link('/trash', t('nav.trash'), <Trash2 size={18} aria-hidden />)}
      {(waiting > 0 || parked > 0 || found > 0) && (
        <Box component={NavLink} to="/sync" sx={navItemSx} onClick={onNavigate}>
          <CloudUpload size={18} aria-hidden />
          {t('sync.nav')}
          {waiting > 0 ? (
            <Chip size="small" color="neutral" label={waiting} aria-label={t('offline.waiting', { count: waiting })} sx={{ ml: 'auto' }} />
          ) : parked > 0 ? (
            <Chip size="small" color="warning" label={parked} aria-label={t('offline.notApplied', { count: parked })} sx={{ ml: 'auto' }} />
          ) : (
            // Only what a restore of the server left on this device (Found on this device): the person decides about it there.
            <Chip size="small" color="neutral" label={found} aria-label={t('sync.found.navCount', { count: found })} sx={{ ml: 'auto' }} />
          )}
        </Box>
      )}
      {user && link('/account', t('nav.account'), <Settings size={18} aria-hidden />)}
      {user?.isAdmin && (
        // The administrators' pages under a heading of their own: a label, not a page, so every entry says what it holds.
        <Stack role="group" aria-labelledby={adminHeading} sx={{ gap: 0.5, mt: 1.5 }}>
          <Typography id={adminHeading} variant="caption" sx={{ px: 1.5, color: 'text.secondary', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.06em' }}>
            {t('nav.admin')}
          </Typography>
          {link('/admin', t('nav.adminOverview'), <ShieldCheck size={18} aria-hidden />)}
          {link('/admin/people', t('nav.people'), <Users size={18} aria-hidden />)}
          {link('/admin/vehicles', t('nav.vehicles'), <Car size={18} aria-hidden />)}
          {link('/admin/settings', t('nav.serverSettings'), <SlidersHorizontal size={18} aria-hidden />)}
          {link('/admin/backups', t('nav.backups'), <DatabaseBackup size={18} aria-hidden />)}
        </Stack>
      )}
      <InstallMenuItem onNavigate={onNavigate} />
      {canSignOut && (
        <Button
          variant="ghost"
          color="neutral"
          sx={(theme) => ({ ...navItemSx(theme), mt: 1.5 })}
          onClick={() => void signOut.start()}
        >
          <LogOut size={18} aria-hidden />
          {t('nav.signOut')}
        </Button>
      )}
      <SignOutConfirm {...signOut.confirm} />
    </Stack>
  )
}
