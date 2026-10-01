import { useQuery } from '@apollo/client/react'
import { Box, Container, Dialog, Flex, Text, VisuallyHidden } from '@radix-ui/themes'
import { motion } from 'motion/react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate, Route, Routes, useLocation, useSearchParams } from 'react-router'
import { useMediaQuery } from './hooks/useMediaQuery.ts'
import { useStoredState } from './hooks/useStoredState.ts'
import { SessionDocument, type SessionQuery } from './gql/generated.ts'
import { LoginView } from './LoginView.tsx'
import { ErrorMessage } from './messages.tsx'
import { NoticeBanner } from './NoticeBanner.tsx'
import { ResetPasswordView } from './PasswordForms.tsx'
import { AccountPage } from './pages/AccountPage.tsx'
import { AdminPanel } from './admin/AdminPanel.tsx'
import { TrashPage } from './pages/TrashPage.tsx'
import { VehiclePage } from './pages/VehiclePage.tsx'
import { VehiclesPage } from './pages/VehiclesPage.tsx'
import { HealthFooter } from './shell/HealthFooter.tsx'
import { NavList } from './shell/NavList.tsx'
import { TopBar } from './shell/TopBar.tsx'

const isBoolean = (value: unknown): value is boolean => typeof value === 'boolean'

export default function App() {
  const { t } = useTranslation()
  const { data, error, loading } = useQuery(SessionDocument)
  const mode = data?.session.mode ?? 'NONE'
  const user = data?.session.user ?? null
  const showMenu = data !== undefined && (mode === 'NONE' || user !== null)

  // Desktop: a docked sidebar, open by default; hiding it is remembered in this browser only.
  // Phone: an overlay drawer, closed until the menu button is pressed.
  const desktop = useMediaQuery('(min-width: 1024px)', true)
  const [dockedOpen, setDockedOpen] = useStoredState('tankstat.nav.open', true, isBoolean)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const navOpen = desktop ? dockedOpen : drawerOpen
  const toggleNav = () => (desktop ? setDockedOpen(!dockedOpen) : setDrawerOpen(!drawerOpen))

  return (
    <Flex direction="column" minHeight="100vh">
      <a className="skip-link" href="#main">
        {t('nav.skip')}
      </a>
      <TopBar user={user} showMenu={showMenu} navOpen={navOpen} onToggleNav={toggleNav} />
      <Flex flexGrow="1" align="stretch">
        {showMenu && desktop && dockedOpen && (
          <Box id="app-nav" className="glass app-sidebar" p="3" width="16rem" flexShrink="0">
            <NavList mode={mode} user={user} />
          </Box>
        )}
        {showMenu && !desktop && (
          <Dialog.Root open={drawerOpen} onOpenChange={setDrawerOpen}>
            <Dialog.Content id="app-nav" className="glass drawer" aria-describedby={undefined}>
              <VisuallyHidden>
                <Dialog.Title>{t('nav.main')}</Dialog.Title>
              </VisuallyHidden>
              <NavList mode={mode} user={user} onNavigate={() => setDrawerOpen(false)} />
            </Dialog.Content>
          </Dialog.Root>
        )}
        <Container asChild size="3" flexGrow="1" p={{ initial: '3', sm: '4' }}>
          <main id="main" tabIndex={-1}>
            {error && <ErrorMessage error={error} />}
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

  if (mode === 'STANDALONE' && resetToken && !user) return <ResetPasswordView token={resetToken} />
  if (mode !== 'NONE' && !user) return <LoginView mode={mode} />

  return (
    // A short fade/slide-in on every page change (honours the user's reduced-motion setting).
    <motion.div key={pathname} initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.2 }}>
      <Routes>
        <Route path="/" element={<Navigate to="/vehicles" replace />} />
        <Route path="/vehicles" element={<VehiclesPage />} />
        <Route path="/vehicles/:id/*" element={<VehiclePage />} />
        <Route path="/trash" element={<TrashPage />} />
        <Route path="/account" element={<AccountPage mode={mode} user={user} />} />
        <Route path="/admin" element={user?.isAdmin ? <AdminPanel /> : <Navigate to="/vehicles" replace />} />
        <Route path="*" element={<Text as="p">{t('app.notFound')}</Text>} />
      </Routes>
    </motion.div>
  )
}
