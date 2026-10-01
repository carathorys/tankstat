import { gql, type TypedDocumentNode } from '@apollo/client'

export type AccessLevel = 'NONE' | 'VIEW' | 'EDIT'

export interface UserAccount {
  id: string
  provider: 'LOCAL' | 'OIDC' | 'PROXY'
  email: string
  displayName: string
  isAdmin: boolean
  isDisabled: boolean
}

export interface AccessGrant {
  id: string
  ownerId: string
  granteeId: string
  level: AccessLevel
}

export interface ResetLink {
  token: string
  url: string | null
  emailSent: boolean
}

export const ADMIN_QUERY: TypedDocumentNode<{
  users: UserAccount[]
  accessSettings: { defaultLevelForOthers: AccessLevel }
  accessGrants: AccessGrant[]
}> = gql`
  query Admin {
    users {
      id
      provider
      email
      displayName
      isAdmin
      isDisabled
    }
    accessSettings {
      defaultLevelForOthers
    }
    accessGrants {
      id
      ownerId
      granteeId
      level
    }
  }
`

export const CREATE_USER_MUTATION: TypedDocumentNode<
  { createUser: { user: UserAccount; reset: ResetLink } },
  { input: { email: string; displayName: string | null; isAdmin: boolean } }
> = gql`
  mutation CreateUser($input: CreateUserInput!) {
    createUser(input: $input) {
      user {
        id
      }
      reset {
        token
        url
        emailSent
      }
    }
  }
`

export const ISSUE_RESET_MUTATION: TypedDocumentNode<{ issuePasswordReset: ResetLink }, { userId: string }> = gql`
  mutation IssuePasswordReset($userId: UUID!) {
    issuePasswordReset(userId: $userId) {
      token
      url
      emailSent
    }
  }
`

export const SET_ADMIN_MUTATION: TypedDocumentNode<{ setUserAdmin: { id: string } }, { userId: string; isAdmin: boolean }> = gql`
  mutation SetUserAdmin($userId: UUID!, $isAdmin: Boolean!) {
    setUserAdmin(userId: $userId, isAdmin: $isAdmin) {
      id
    }
  }
`

export const SET_DISABLED_MUTATION: TypedDocumentNode<{ setUserDisabled: { id: string } }, { userId: string; disabled: boolean }> = gql`
  mutation SetUserDisabled($userId: UUID!, $disabled: Boolean!) {
    setUserDisabled(userId: $userId, disabled: $disabled) {
      id
    }
  }
`

export const SET_DEFAULT_ACCESS_MUTATION: TypedDocumentNode<
  { setDefaultAccess: { defaultLevelForOthers: AccessLevel } },
  { level: AccessLevel }
> = gql`
  mutation SetDefaultAccess($level: AccessLevel!) {
    setDefaultAccess(level: $level) {
      defaultLevelForOthers
    }
  }
`

export const SET_GRANT_MUTATION: TypedDocumentNode<
  { setAccessGrant: boolean },
  { input: { ownerId: string; granteeId: string; level: AccessLevel } }
> = gql`
  mutation SetAccessGrant($input: SetAccessGrantInput!) {
    setAccessGrant(input: $input)
  }
`
