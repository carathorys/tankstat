import { graphql as msw } from 'msw/graphql'

/**
 * The fakes' GraphQL handlers (`graphql.query('Welcome', ...)`, `graphql.mutation(...)`), scoped to the app's endpoint: msw 3 only
 * intercepts GraphQL through a link to a URL. `*` takes both `/graphql` (`createApolloClient`'s default, against jsdom's origin) and
 * the `http://localhost/graphql` the tests build their clients with.
 */
export const graphql = msw.link('*/graphql')
