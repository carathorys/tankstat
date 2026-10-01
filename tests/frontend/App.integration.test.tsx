import { ApolloProvider } from '@apollo/client/react'
import { render, screen } from '@testing-library/react'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, it } from 'vitest'
import App from '../../src/frontend/App.tsx'
import { createApolloClient } from '../../src/frontend/apolloClient.ts'
import { server } from './server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

// Fresh client (and cache) per test, talking to a mocked GraphQL endpoint over real HTTP.
const renderApp = () =>
  render(
    <ApolloProvider client={createApolloClient('http://localhost/graphql')}>
      <App />
    </ApolloProvider>,
  )

it('renders API health fetched over GraphQL', async () => {
  server.use(
    graphql.query('Health', () =>
      HttpResponse.json({ data: { health: { status: 'ok', version: '1.2.3', databaseReachable: true } } }),
    ),
  )
  renderApp()
  await screen.findByText('Healthy')
  await screen.findByText(/v1\.2\.3/)
})

it('shows an alert when the API returns a GraphQL error', async () => {
  server.use(graphql.query('Health', () => HttpResponse.json({ errors: [{ message: 'boom' }] })))
  renderApp()
  await screen.findByRole('alert')
})

it('shows an alert when the API is down', async () => {
  server.use(graphql.query('Health', () => new HttpResponse(null, { status: 500 })))
  renderApp()
  await screen.findByRole('alert')
})
