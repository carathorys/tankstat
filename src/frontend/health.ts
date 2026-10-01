import { gql, type TypedDocumentNode } from '@apollo/client'

export interface HealthData {
  health: { status: string; version: string; databaseReachable: boolean }
}

export const HEALTH_QUERY: TypedDocumentNode<HealthData> = gql`
  query Health {
    health {
      status
      version
      databaseReachable
    }
  }
`
