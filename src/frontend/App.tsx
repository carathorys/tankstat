import { useQuery } from '@apollo/client/react'
import { Box, Container, Dialog, Flex, IconButton, Text } from '@radix-ui/themes'
import { X } from 'lucide-react'
import { motion } from 'motion/react'
import { lazy, Suspense, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate, Route, Routes, useLocation, useSearchParams } from 'react-router'
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
  // Desktop: a docked sidebar, open by default; hiding it is remembered (in this browser, and with the account once signed in).
  // Phone: an overlay drawer, closed until the menu button is pressed, never remembered.
  const desktop = useMediaQuery('(min-width: 1024px)', true)
  const settings = useUiSettings()!
  const dockedOpen = settings.navOpen
  const [drawerOpen, setDrawerOpen] = useState(false)
  const navOpen = desktop ? dockedOpen : drawerOpen
  const toggleNav = () => (desktop ? settings.setNavOpen(!dockedOpen) : setDrawerOpen(!drawerOpen))

  return (
    <Flex direction="column" minHeight="100vh">
      <a className="skip-link" href="#main">
        {t('nav.skip')}
      </a>
      <TopBar user={user} showMenu={showMenu} navOpen={navOpen} onToggleNav={toggleNav} />
      <Flex flexGrow="1" align="stretch" style={{ minWidth: 0 }}>
        {showMenu && desktop && dockedOpen && (
          <Box id="app-nav" className="glass app-sidebar" p="3" width="16rem" flexShrink="0">
            <NavList mode={mode} user={user} />
          </Box>
        )}
        {showMenu && !desktop && (
          <Dialog.Root open={drawerOpen} onOpenChange={setDrawerOpen}>
            <Dialog.Content id="app-nav" className="glass drawer" aria-describedby={undefined}>
              <Flex justify="between" align="center" mb="3">
                <Dialog.Title size="4" mb="0">
                  {t('app.name')}
                </Dialog.Title>
                <Dialog.Close>
                  <IconButton variant="ghost" color="gray" size="3" aria-label={t('nav.close')}>
                    <X size={20} aria-hidden />
                  </IconButton>
                </Dialog.Close>
              </Flex>
              <NavList mode={mode} user={user} onNavigate={() => setDrawerOpen(false)} />
            </Dialog.Content>
          </Dialog.Root>
        )}
        <Container asChild size="4" flexGrow="1" p={{ initial: '3', sm: '4' }} minWidth="0" style={{ minWidth: 0, maxWidth: '100%' }}>
          <main id="main" tabIndex={-1}>
            {error !== undefined && <ErrorMessage error={error} />}
            {loading && (
              <Text as="p" role="status">
                {t('app.loading')}
              </Text>
            )}
            {data && <NoticeBanner notices={data.notices} />}
            {data && <Content data={data} />}
          </main>
        </Container>
      </Flex>
      <HealthFooter />
    </Flex>
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
            <Text as="p" role="status">
              {t('app.loading')}
            </Text>
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
            <Route path="*" element={<Text as="p">{t('app.notFound')}</Text>} />
          </Routes>
        </Suspense>
      </ErrorBoundary>
    </motion.div>
  )
}
