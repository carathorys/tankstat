import type { CodegenConfig } from '@graphql-codegen/cli'

/**
 * All GraphQL types and typed documents are generated from the backend schema (schema.graphql at the repository root, exported by
 * `mise run schema:export`) and the operations in src/graphql/*.graphql. Nothing is typed by hand.
 */
const config: CodegenConfig = {
  schema: '../../../schema.graphql',
  documents: 'src/graphql/*.graphql',
  generates: {
    'src/gql/generated.ts': {
      plugins: ['typescript-operations', 'typed-document-node'],
      config: {
        enumsAsTypes: true,
        useTypeImports: true,
        skipTypename: true,
        scalars: { UUID: 'string', DateTime: 'string', LocalDate: 'string', Decimal: 'number', Duration: 'string', Long: 'number', Any: 'unknown' },
      },
    },
  },
}

export default config
