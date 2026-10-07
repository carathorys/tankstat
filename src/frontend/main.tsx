// First of all: Chromium offers to install the app once, early, long before the menu that shows the offer exists.
import './pwa/installPrompt.ts'
import { ApolloProvider } from '@apollo/client/react'
import { MotionConfig } from 'motion/react'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'
import { registerSW } from 'virtual:pwa-register'
import App from './App.tsx'
import { createApolloClient } from './apolloClient.ts'
import { initI18n } from './i18n/index.ts'
import { appUpdate } from './pwa/appUpdate.ts'
import { watchForUpdates } from './pwa/watchForUpdates.ts'
import { ThemeRoot } from './theme/ThemeRoot.tsx'

// Installable web app: the service worker keeps the built shell. A new release is downloaded in the background and offered to the person
// (UpdateNotice); a page that stays open asks for one every hour and when it is shown again (see vite.config.ts, pwa/watchForUpdates.ts).
const updateSW = registerSW({
  immediate: true,
  onNeedRefresh: () =>
    appUpdate.offer(() => {
      // The page reloads as soon as the new worker has taken over, whatever the library thinks of this being the first update.
      navigator.serviceWorker.addEventListener('controllerchange', () => window.location.reload(), { once: true })
      void updateSW(true)
    }),
  onRegisteredSW: (_url, registration) => {
    if (registration) watchForUpdates(registration)
  },
})

// The translations are ready before the first render, so the UI never flashes untranslated.
await initI18n()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeRoot>
      <MotionConfig reducedMotion="user">
        <ApolloProvider client={createApolloClient()}>
          <BrowserRouter>
            <App />
          </BrowserRouter>
        </ApolloProvider>
      </MotionConfig>
    </ThemeRoot>
  </StrictMode>,
)
