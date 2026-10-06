// First of all: Chromium offers to install the app once, early, long before the menu that shows the offer exists.
import './pwa/installPrompt.ts'
import { ApolloProvider } from '@apollo/client/react'
import '@radix-ui/themes/styles.css'
import { MotionConfig } from 'motion/react'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'
import { registerSW } from 'virtual:pwa-register'
import App from './App.tsx'
import { createApolloClient } from './apolloClient.ts'
import { initI18n } from './i18n/index.ts'
import { RadixTheme } from './theme/RadixTheme.tsx'
import { ThemeRoot } from './theme/ThemeRoot.tsx'

// Installable web app: the service worker keeps the built shell and takes over with every release (see vite.config.ts).
registerSW({ immediate: true })

// The translations are ready before the first render, so the UI never flashes untranslated.
await initI18n()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeRoot>
      <RadixTheme>
        <MotionConfig reducedMotion="user">
          <ApolloProvider client={createApolloClient()}>
            <BrowserRouter>
              <App />
            </BrowserRouter>
          </ApolloProvider>
        </MotionConfig>
      </RadixTheme>
    </ThemeRoot>
  </StrictMode>,
)
