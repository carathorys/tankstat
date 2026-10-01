import { useQuery } from '@apollo/client/react'
import { Container, Flex, Text } from '@radix-ui/themes'
import { motion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { Navigate, Route, Routes, useLocation, useSearchParams } from 'react-router'
import { SessionDocument, type SessionQuery } from './gql/generated.ts'
import { LoginView } from './LoginView.tsx'
import { ErrorMessage } from './messages.tsx'
import { NoticeBanner } from './NoticeBanner.tsx'
import { ResetPasswordView } from './PasswordForms.tsx'
import { AccountPage } from './pages/AccountPage.tsx'
import { AdminPanel } from './admin/AdminPanel.tsx'
import { TrashPage } from './pages/TrashPage.tsx'
import { VehiclesPage } from './pages/VehiclesPage.tsx'
import { HealthFooter } from './shell/HealthFooter.tsx'
import { TopBar } from './shell/TopBar.tsx'

export default function App() {
  const { t } = useTranslation()
  const { data, error, loading } = useQuery(SessionDocument)
  const mode = data?.session.mode ?? 'NONE'
  const user = data?.session.user ?? null
  const showMenu = data !== undefined && (mode === 'NONE' || user !== null)

  return (
    <Flex direction="column" minHeight="100vh">
      <TopBar mode={mode} user={user} showMenu={showMenu} />
      <Container asChild size="3" flexGrow="1" p={{ initial: '3', sm: '4' }}>
        <main>
          {error && <ErrorMessage error={error} />}
          {loading && <Text as="p">{t('app.loading')}</Text>}
          {data && <NoticeBanner notices={data.notices} />}
          {data && <Content data={data} />}
        </main>
      </Container>
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
        <Route path="/trash" element={<TrashPage />} />
        <Route path="/account" element={<AccountPage mode={mode} user={user} />} />
        <Route path="/admin" element={user?.isAdmin ? <AdminPanel /> : <Navigate to="/vehicles" replace />} />
        <Route path="*" element={<Text as="p">{t('app.notFound')}</Text>} />
      </Routes>
    </motion.div>
  )
}
