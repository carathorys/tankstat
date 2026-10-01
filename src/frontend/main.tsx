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
import { ThemeProvider } from "next-themes";

// The translations are ready before the first render, so the UI never flashes untranslated.
await initI18n()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider attribute="class">
      <Theme accentColor="indigo" grayColor="sand" appearance="dark" radius="full" panelBackground="translucent" className="dark">
        <MotionConfig reducedMotion="user">
          <ApolloProvider client={createApolloClient()}>
            <BrowserRouter>
              <App />
            </BrowserRouter>
          </ApolloProvider>
        </MotionConfig>
      </Theme>
    </ThemeProvider>
  </StrictMode>,
)
