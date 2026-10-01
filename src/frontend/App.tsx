import { useQuery } from '@apollo/client/react'
import { Navigate, Route, Routes, useSearchParams } from 'react-router'
import { LoginView } from './LoginView.tsx'
import { NoticeBanner } from './NoticeBanner.tsx'
import { ResetPasswordView } from './PasswordForms.tsx'
import { AccountPage } from './pages/AccountPage.tsx'
import { AdminPage } from './pages/AdminPage.tsx'
import { TrashPage } from './pages/TrashPage.tsx'
import { VehiclesPage } from './pages/VehiclesPage.tsx'
import { SESSION_QUERY, type SessionData } from './session.ts'
import { HealthFooter } from './shell/HealthFooter.tsx'
import { TopBar } from './shell/TopBar.tsx'

export default function App() {
  const { data, error, loading } = useQuery(SESSION_QUERY)
  const mode = data?.session.mode ?? 'NONE'
  const user = data?.session.user ?? null
  const showMenu = data !== undefined && (mode === 'NONE' || user !== null)

  return (
    <div className="app">
      <TopBar mode={mode} user={user} showMenu={showMenu} />
      <main>
        {error && <p role="alert">{error.message}</p>}
        {loading && <p>Loading…</p>}
        {data && <NoticeBanner notices={data.notices} />}
        {data && <Content data={data} />}
      </main>
      <HealthFooter />
    </div>
  )
}

function Content({ data }: { data: SessionData }) {
  const [params] = useSearchParams()
  const resetToken = params.get('resetToken')
  const { mode, user } = data.session

  if (mode === 'STANDALONE' && resetToken && !user) return <ResetPasswordView token={resetToken} />
  if (mode !== 'NONE' && !user) return <LoginView mode={mode} />

  return (
    <Routes>
      <Route path="/" element={<Navigate to="/vehicles" replace />} />
      <Route path="/vehicles" element={<VehiclesPage />} />
      <Route path="/trash" element={<TrashPage />} />
      <Route path="/account" element={<AccountPage mode={mode} user={user} />} />
      <Route path="/admin" element={user?.isAdmin ? <AdminPage /> : <Navigate to="/vehicles" replace />} />
      <Route path="*" element={<p>This page does not exist.</p>} />
    </Routes>
  )
}
