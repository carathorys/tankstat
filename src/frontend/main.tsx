// First of all: Chromium offers to install the app once, early, long before the menu that shows the offer exists.
import './pwa/installPrompt.ts'
import { ApolloProvider } from '@apollo/client/react'
import { MotionConfig } from 'motion/react'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'
import App from './App.tsx'
import { createApolloClient } from './apolloClient.ts'
import { deviceData } from './offline/deviceData.ts'
import { startOfflineRuntime } from './offline/runtime.ts'
import { initI18n } from './i18n/index.ts'
import { registerApp } from './pwa/registerApp.ts'
import { watchForUpdates } from './pwa/watchForUpdates.ts'
import { ThemeRoot } from './theme/ThemeRoot.tsx'

// Installable web app: the service worker keeps the built shell. A new release is downloaded in the background and offered to the person
// (UpdateNotice); a page that stays open asks for one every hour and when it is shown again (see vite.config.ts, pwa/registerApp.ts,
// pwa/watchForUpdates.ts). The development server has no service worker, and neither has a page over plain HTTP.
if (import.meta.env.PROD && 'serviceWorker' in navigator) {
  void registerApp({ container: navigator.serviceWorker, onRegistered: (registration) => watchForUpdates(registration) })
}

// The translations are ready before the first render, so the UI never flashes untranslated; the data this device kept for the last
// user is open before the first request, so the app opens with it while the server is out of reach (see offline/deviceData.ts).
await Promise.all([initI18n(), deviceData.boot()])

// One client for the page; while the server is out of reach it is asked again now and then (see offline/connectivity.ts).
const client = createApolloClient()
startOfflineRuntime(client)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeRoot>
      <MotionConfig reducedMotion="user">
        <ApolloProvider client={client}>
          <BrowserRouter>
            <App />
          </BrowserRouter>
        </ApolloProvider>
      </MotionConfig>
    </ThemeRoot>
  </StrictMode>,
)
