import { ApolloProvider } from '@apollo/client/react'
import '@radix-ui/themes/styles.css'
import './main.css'
import { Theme } from '@radix-ui/themes'
import { MotionConfig } from 'motion/react'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'
import App from './App.tsx'
import { createApolloClient } from './apolloClient.ts'
import { initI18n } from './i18n/index.ts'

// The translations are ready before the first render, so the UI never flashes untranslated.
await initI18n()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Theme accentColor="blue" appearance="dark">
      <MotionConfig reducedMotion="user">
        <ApolloProvider client={createApolloClient()}>
          <BrowserRouter>
            <App />
          </BrowserRouter>
        </ApolloProvider>
      </MotionConfig>
    </Theme>
  </StrictMode>,
)
