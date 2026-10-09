import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import { CloudOff, TriangleAlert } from 'lucide-react'
import { Component, type ErrorInfo, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Loading } from './components/Loading.tsx'
import { isChunkLoadError, recoverFromChunkError, type ChunkRecovery } from './pwa/chunkRecovery.ts'

/** What the user sees when a page fails to render: a message and a way out, instead of a blank screen. */
function CrashMessage({ onReload }: { onReload: () => void }) {
  const { t } = useTranslation()
  return (
    <Alert severity="error" role="alert" icon={<TriangleAlert size={16} aria-hidden />} sx={{ my: 1 }}>
      <Stack sx={{ alignItems: 'flex-start', gap: 1.5 }}>
        <span>{t('app.crashed')}</span>
        <Button onClick={onReload}>{t('app.reload')}</Button>
      </Stack>
    </Alert>
  )
}

/** Offline, a page whose code this device has not kept yet: not an error, it opens once the server can be reached. */
function NotOfflineYet() {
  const { t } = useTranslation()
  return (
    <Alert severity="info" role="status" icon={<CloudOff size={16} aria-hidden />} sx={{ my: 1 }}>
      {t('app.notOfflineYet')}
    </Alert>
  )
}

interface BoundaryState {
  failed: boolean
  /** The page's code could not be loaded (see `chunkRecovery.ts`). */
  chunk: boolean
  /** What was done about it: decided right after the failure, so null only for a moment. */
  recovery: ChunkRecovery | null
}

/**
 * Catches a page that throws while rendering, shows a message with a reload button and writes the cause to the browser console. A page
 * whose code cannot be loaded (a newer release took its place, or it is not kept on this device yet) is reloaded once instead, or said to
 * be not available offline yet (`recoverFromChunkError`). Meant for one page, below the shell: give it a `key` that changes with the route
 * and the next page starts fresh. `onReload` is there for tests; in the app it reloads the page.
 */
export class ErrorBoundary extends Component<{ children: ReactNode; onReload?: () => void }, BoundaryState> {
  state: BoundaryState = { failed: false, chunk: false, recovery: null }

  static getDerivedStateFromError(error: unknown): Partial<BoundaryState> {
    return { failed: true, chunk: isChunkLoadError(error), recovery: null }
  }

  componentDidCatch(error: unknown, info: ErrorInfo) {
    const recovery = this.state.chunk ? recoverFromChunkError({ reload: this.props.onReload }) : 'failed'
    if (this.state.chunk) this.setState({ recovery })
    if (recovery === 'failed') console.error('Rendering a page failed', error, info.componentStack)
    else console.warn(recovery === 'reloading' ? 'A page\'s code could not be loaded; reloading for the current version.' : 'A page is not kept on this device yet.', error)
  }

  render() {
    if (!this.state.failed) return this.props.children
    if (this.state.chunk && (this.state.recovery === null || this.state.recovery === 'reloading')) return <Loading />
    if (this.state.chunk && this.state.recovery === 'notOfflineYet') return <NotOfflineYet />
    return <CrashMessage onReload={this.props.onReload ?? (() => window.location.reload())} />
  }
}
