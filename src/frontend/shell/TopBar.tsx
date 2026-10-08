import Box from '@mui/material/Box'
import Chip from '@mui/material/Chip'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { Menu } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { IconAction } from '../components/IconAction.tsx'
import { UserAvatar } from '../components/UserAvatar.tsx'
import type { SessionQuery } from '../gql/generated.ts'
import { useMediaQuery } from '../hooks/useMediaQuery.ts'
import { glass } from '../theme/components.ts'
import { MEDIA } from '../theme/media.ts'
import { ColorModeMenu } from './ColorModeMenu.tsx'
import { ConnectivityIndicator } from './ConnectivityIndicator.tsx'
import { PendingChip } from './PendingChip.tsx'
import { LanguageMenu } from './LanguageMenu.tsx'
import { NotificationBell } from './NotificationBell.tsx'

/**
 * Shown on every screen; the menu button appears once the visitor may use the app (signed in, or auth is off). Installed on a phone
 * (viewport-fit=cover in index.html) it keeps below the notch and the status bar.
 */
export function TopBar({
  user,
  showMenu,
  navOpen,
  onToggleNav,
}: {
  user: SessionQuery['session']['user']
  showMenu: boolean
  navOpen: boolean
  onToggleNav: () => void
}) {
  const { t } = useTranslation()
  // A phone has no room for everything in one row: the badges shrink to their icons (their names stay for screen readers), and the
  // colour mode and language menus move into the navigation drawer (`DrawerSettings`) while there is one to open.
  const compact = useMediaQuery(MEDIA.narrow, false)
  const menusInDrawer = compact && showMenu

  return (
    <Box
      component="header"
      sx={(theme) => ({
        ...glass(theme),
        boxShadow: theme.shadows[3],
        position: 'sticky',
        top: 0,
        zIndex: theme.zIndex.appBar,
        display: 'flex',
        alignItems: 'center',
        gap: compact ? 1 : 1.5,
        // Never wider than the screen: a bar that is would let the whole page scroll sideways.
        minWidth: 0,
        overflowX: 'clip',
        borderBottom: `1px solid ${theme.vars.palette.neutral.softHover}`,
        padding: 'calc(8px + env(safe-area-inset-top, 0px)) calc(16px + env(safe-area-inset-right, 0px)) 8px calc(16px + env(safe-area-inset-left, 0px))',
      })}
    >
      {showMenu && (
        <IconAction label={navOpen ? t('nav.hide') : t('nav.show')} size="large" aria-expanded={navOpen} aria-controls="app-nav" onClick={onToggleNav}>
          <Menu size={20} aria-hidden />
        </IconAction>
      )}
      {/* The one thing that gives way when the bar is full: the name, cut short, never the controls. */}
      <Typography component={Link} to="/" variant="h4" noWrap sx={{ color: 'inherit', textDecoration: 'none', minWidth: 0, flexShrink: 1 }}>
        {t('app.name')}
      </Typography>
      <Box sx={{ flexGrow: 1 }} />
      {user && (
        <Stack direction="row" role="group" aria-label={t('nav.signedInAs', { name: user.displayName })} sx={{ alignItems: 'center', gap: 1, flexShrink: 0 }}>
          <UserAvatar user={user} />
          <Typography variant="body2" sx={{ display: { xs: 'none', sm: 'inline' } }}>
            {user.displayName}
          </Typography>
          {user.isAdmin && !compact && <Chip color="neutral" label={t('role.adminBadge')} />}
        </Stack>
      )}
      <ConnectivityIndicator compact={compact} />
      <PendingChip parked={showMenu} compact={compact} />
      {showMenu && <NotificationBell />}
      {!menusInDrawer && (
        <>
          <ColorModeMenu />
          <LanguageMenu />
        </>
      )}
    </Box>
  )
}

/** The colour mode and language menus in the navigation drawer, on a phone, where the top bar has no room for them. */
export function DrawerSettings() {
  const compact = useMediaQuery(MEDIA.narrow, false)
  if (!compact) return null
  return (
    <Stack direction="row" sx={{ gap: 1, mt: 2 }}>
      <ColorModeMenu />
      <LanguageMenu />
    </Stack>
  )
}
