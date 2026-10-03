import { Button, Callout, Flex } from '@radix-ui/themes'
import { TriangleAlert } from 'lucide-react'
import { Component, type ErrorInfo, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

/** What the user sees when a page fails to render: a message and a way out, instead of a blank screen. */
function CrashMessage({ onReload }: { onReload: () => void }) {
  const { t } = useTranslation()
  return (
    <Callout.Root color="red" role="alert" my="2">
      <Callout.Icon>
        <TriangleAlert size={16} />
      </Callout.Icon>
      <Flex direction="column" align="start" gap="3">
        <Callout.Text>{t('app.crashed')}</Callout.Text>
        <Button onClick={onReload}>{t('app.reload')}</Button>
      </Flex>
    </Callout.Root>
  )
}

/**
 * Catches a page that throws while rendering (and a page chunk that cannot be loaded), shows a message with a reload button and writes the
 * cause to the browser console. Meant for one page, below the shell: give it a `key` that changes with the route and the next page starts
 * fresh. `onReload` is there for tests; in the app it reloads the page.
 */
export class ErrorBoundary extends Component<{ children: ReactNode; onReload?: () => void }, { failed: boolean }> {
  state = { failed: false }

  static getDerivedStateFromError() {
    return { failed: true }
  }

  componentDidCatch(error: unknown, info: ErrorInfo) {
    console.error('Rendering a page failed', error, info.componentStack)
  }

  render() {
    if (!this.state.failed) return this.props.children
    return <CrashMessage onReload={this.props.onReload ?? (() => window.location.reload())} />
  }
}
