import type { CodegenConfig } from '@graphql-codegen/cli'

/**
 * All GraphQL types and typed documents are generated from the backend schema (schema.graphql, exported by
 * `mise run schema:export`) and the operations in src/frontend/graphql/*.graphql. Nothing is typed by hand.
 */
const config: CodegenConfig = {
  schema: 'schema.graphql',
  documents: 'src/frontend/graphql/*.graphql',
  generates: {
    'src/frontend/gql/generated.ts': {
      plugins: ['typescript-operations', 'typed-document-node'],
      config: {
        enumsAsTypes: true,
        useTypeImports: true,
        skipTypename: true,
        scalars: { UUID: 'string', DateTime: 'string', LocalDate: 'string', Decimal: 'number', Duration: 'string' },
      },
    },
  },
}

export default config
