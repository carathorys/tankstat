import { useQuery } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Drawer from '@mui/material/Drawer'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { X } from 'lucide-react'
import { motion } from 'motion/react'
import { lazy, Suspense, useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate, Route, Routes, useLocation, useSearchParams } from 'react-router'
import { IconAction } from './components/IconAction.tsx'
import { useMediaQuery } from './hooks/useMediaQuery.ts'
import { SessionDocument, type AuthMode, type SessionQuery } from './gql/generated.ts'
import { ErrorBoundary } from './ErrorBoundary.tsx'
import { LoginView } from './LoginView.tsx'
import { ErrorMessage } from './messages.tsx'
import { NoticeBanner } from './NoticeBanner.tsx'
import { ResetPasswordView } from './PasswordForms.tsx'
import { HealthFooter } from './shell/HealthFooter.tsx'
import { UiSettingsProvider } from './settings/UiSettingsProvider.tsx'
import { useUiSettings } from './settings/uiSettingsContext.ts'
import { NavList } from './shell/NavList.tsx'
import { TopBar } from './shell/TopBar.tsx'
import { UpdateNotice } from './shell/UpdateNotice.tsx'
import { Loading } from './components/Loading.tsx'
import { glass } from './theme/components.ts'
import { MEDIA } from './theme/media.ts'

// Every page is its own chunk, loaded when it is first visited: the first screen only needs the shell and the page it opens on.
const WelcomePage = lazy(() => import('./pages/WelcomePage.tsx').then((m) => ({ default: m.WelcomePage })))
const VehiclesPage = lazy(() => import('./pages/VehiclesPage.tsx').then((m) => ({ default: m.VehiclesPage })))
const VehiclePage = lazy(() => import('./pages/VehiclePage.tsx').then((m) => ({ default: m.VehiclePage })))
const TrashPage = lazy(() => import('./pages/TrashPage.tsx').then((m) => ({ default: m.TrashPage })))
const ImportPage = lazy(() => import('./pages/ImportPage.tsx').then((m) => ({ default: m.ImportPage })))
const NotificationsPage = lazy(() => import('./pages/NotificationsPage.tsx').then((m) => ({ default: m.NotificationsPage })))
const AccountPage = lazy(() => import('./pages/AccountPage.tsx').then((m) => ({ default: m.AccountPage })))
const AdminPanel = lazy(() => import('./admin/AdminPanel.tsx').then((m) => ({ default: m.AdminPanel })))

export default function App() {
  const { data, error, loading } = useQuery(SessionDocument)
  const mode = data?.session.mode ?? 'NONE'
  const user = data?.session.user ?? null
  const showMenu = data !== undefined && (mode === 'NONE' || user !== null)

  // The UI settings follow the user: asked from the server once per session, kept per user (the key starts afresh on sign-in and sign-out).
  return (
    <UiSettingsProvider key={user?.id ?? mode} enabled={showMenu}>
      <Shell data={data} error={error} loading={loading} mode={mode} user={user} showMenu={showMenu} />
    </UiSettingsProvider>
  )
}

function Shell({
  data,
  error,
  loading,
  mode,
  user,
  showMenu,
}: {
  data: SessionQuery | undefined
  error: unknown
  loading: boolean
  mode: AuthMode
  user: SessionQuery['session']['user']
  showMenu: boolean
}) {
  const { t } = useTranslation()
  const drawerTitle = useId()
  // Desktop: a docked sidebar, open by default; hiding it is remembered (in this browser, and with the account once signed in).
  // Phone: an overlay drawer, closed until the menu button is pressed, never remembered.
  const desktop = useMediaQuery(MEDIA.desktop, true)
  const settings = useUiSettings()!
  const dockedOpen = settings.navOpen
  const [drawerOpen, setDrawerOpen] = useState(false)
  const navOpen = desktop ? dockedOpen : drawerOpen
  const toggleNav = () => (desktop ? settings.setNavOpen(!dockedOpen) : setDrawerOpen(!drawerOpen))

  return (
    <Stack sx={{ minHeight: '100vh' }}>
      <SkipLink />
      <TopBar user={user} showMenu={showMenu} navOpen={navOpen} onToggleNav={toggleNav} />
      <Box sx={{ display: 'flex', flexGrow: 1, alignItems: 'stretch', minWidth: 0 }}>
        {showMenu && desktop && dockedOpen && (
          <Box
            id="app-nav"
            sx={(theme) => ({
              ...glass(theme),
              boxShadow: theme.shadows[4],
              borderRight: `1px solid ${theme.vars.palette.neutral.softHover}`,
              p: 1.5,
              width: '16rem',
              flexShrink: 0,
            })}
          >
            <NavList mode={mode} user={user} />
          </Box>
        )}
        {showMenu && !desktop && (
          <Drawer
            open={drawerOpen}
            onClose={() => setDrawerOpen(false)}
            slotProps={{
              paper: {
                id: 'app-nav',
                role: 'dialog',
                'aria-modal': true,
                'aria-labelledby': drawerTitle,
                // Installed on a phone the drawer runs edge to edge: its content keeps clear of the notch and the home indicator.
                sx: (theme) => ({
                  ...glass(theme),
                  width: 'min(20rem, 85vw)',
                  pt: 'calc(20px + env(safe-area-inset-top, 0px))',
                  pb: 'calc(24px + env(safe-area-inset-bottom, 0px))',
                  pl: 'calc(24px + env(safe-area-inset-left, 0px))',
                  pr: 3,
                }),
              },
            }}
          >
            <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1.5 }}>
              <Typography id={drawerTitle} component="h2" variant="h5">
                {t('app.name')}
              </Typography>
              {/* A ghost button reaches into the padding, so the row is only as tall as the title. */}
              <IconAction label={t('nav.close')} variant="ghost" size="large" sx={{ m: -1 }} onClick={() => setDrawerOpen(false)}>
                <X size={20} aria-hidden />
              </IconAction>
            </Stack>
            <NavList mode={mode} user={user} onNavigate={() => setDrawerOpen(false)} />
          </Drawer>
        )}
        {/* The page column (at most 1136 px, centred), clear of a phone's rounded corners in landscape. */}
        <Box
          component="main"
          id="main"
          tabIndex={-1}
          sx={{
            flexGrow: 1,
            minWidth: 0,
            py: { xs: 1.5, md: 2 },
            pl: { xs: 'max(12px, env(safe-area-inset-left, 0px))', md: 'max(16px, env(safe-area-inset-left, 0px))' },
            pr: { xs: 'max(12px, env(safe-area-inset-right, 0px))', md: 'max(16px, env(safe-area-inset-right, 0px))' },
          }}
        >
          <Box sx={{ maxWidth: 1136, mx: 'auto' }}>
            {error !== undefined && <ErrorMessage error={error} />}
            {loading && <Loading />}
            <UpdateNotice />
            {data && <NoticeBanner notices={data.notices} />}
            {data && <Content data={data} />}
          </Box>
        </Box>
      </Box>
      <HealthFooter />
    </Stack>
  )
}

/** "Skip to main content": out of sight until it has the keyboard focus, then at the top left, clear of a notch. */
function SkipLink() {
  const { t } = useTranslation()
  return (
    <Box
      component="a"
      href="#main"
      sx={(theme) => ({
        ...theme.typography.body2,
        position: 'absolute',
        left: 12,
        top: -100,
        zIndex: theme.zIndex.tooltip,
        px: 1.5,
        py: 1,
        borderRadius: '9px',
        backgroundColor: theme.vars.palette.primary.main,
        color: theme.vars.palette.primary.contrastText,
        '&:focus': { top: 'calc(12px + env(safe-area-inset-top, 0px))' },
      })}
    >
      {t('nav.skip')}
    </Box>
  )
}

function Content({ data }: { data: SessionQuery }) {
  const { t } = useTranslation()
  const { pathname } = useLocation()
  const [params] = useSearchParams()
  const resetToken = params.get('resetToken')
  const { mode, user } = data.session
  const isAdmin = user?.isAdmin === true // the full vehicle list and the administration are for administrators only

  if (mode === 'STANDALONE' && resetToken && !user) return <ResetPasswordView token={resetToken} />
  if (mode !== 'NONE' && !user) return <LoginView mode={mode} />

  return (
    // A short fade/slide-in on every page change (honours the user's reduced-motion setting).
    // The key makes the boundary start fresh with every page, so one that crashed does not hold the next one hostage.
    <motion.div key={pathname} initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.2 }}>
      <ErrorBoundary>
        <Suspense
          fallback={
            <Loading />
          }
        >
          <Routes>
            <Route path="/" element={<WelcomePage isAdmin={isAdmin} />} />
            <Route path="/vehicles" element={isAdmin ? <VehiclesPage /> : <Navigate to="/" replace />} />
            <Route path="/vehicles/:id/*" element={<VehiclePage isAdmin={isAdmin} />} />
            <Route path="/import" element={<ImportPage />} />
            <Route path="/trash" element={<TrashPage />} />
            <Route path="/notifications" element={<NotificationsPage />} />
            <Route path="/account" element={<AccountPage mode={mode} user={user} />} />
            <Route path="/admin" element={isAdmin ? <AdminPanel /> : <Navigate to="/" replace />} />
            <Route path="*" element={<Typography>{t('app.notFound')}</Typography>} />
          </Routes>
        </Suspense>
      </ErrorBoundary>
    </motion.div>
  )
}
