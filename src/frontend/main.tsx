import { ApolloProvider } from '@apollo/client/react'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App.tsx'
import { createApolloClient } from './apolloClient.ts'
import './index.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ApolloProvider client={createApolloClient()}>
      <App />
    </ApolloProvider>
  </StrictMode>,
)
