import { ApolloClient, HttpLink, InMemoryCache } from '@apollo/client'

export function createApolloClient(uri = '/graphql') {
  return new ApolloClient({ link: new HttpLink({ uri }), cache: new InMemoryCache() })
}
