import { gql, type TypedDocumentNode } from '@apollo/client'

export type AuthMode = 'NONE' | 'STANDALONE' | 'OIDC' | 'PROXY_HEADER'

export interface SessionUser {
  id: string
  displayName: string
  email: string
  isAdmin: boolean
}

export interface Notice {
  code: string
  severity: 'INFO' | 'WARNING'
  message: string
}

export interface SessionData {
  session: { mode: AuthMode; user: SessionUser | null }
  notices: Notice[]
}

export const SESSION_QUERY: TypedDocumentNode<SessionData> = gql`
  query Session {
    session {
      mode
      user {
        id
        displayName
        email
        isAdmin
      }
    }
    notices {
      code
      severity
      message
    }
  }
`

export const LOGIN_MUTATION: TypedDocumentNode<{ login: { id: string } }, { input: { email: string; password: string } }> = gql`
  mutation Login($input: LoginInput!) {
    login(input: $input) {
      id
    }
  }
`

export const LOGOUT_MUTATION: TypedDocumentNode<{ logout: boolean }> = gql`
  mutation Logout {
    logout
  }
`

export const CHANGE_PASSWORD_MUTATION: TypedDocumentNode<
  { changePassword: boolean },
  { input: { currentPassword: string; newPassword: string } }
> = gql`
  mutation ChangePassword($input: ChangePasswordInput!) {
    changePassword(input: $input)
  }
`

export const REQUEST_RESET_MUTATION: TypedDocumentNode<{ requestPasswordReset: boolean }, { email: string }> = gql`
  mutation RequestPasswordReset($email: String!) {
    requestPasswordReset(email: $email)
  }
`

export const RESET_PASSWORD_MUTATION: TypedDocumentNode<
  { resetPassword: boolean },
  { input: { token: string; newPassword: string } }
> = gql`
  mutation ResetPassword($input: ResetPasswordInput!) {
    resetPassword(input: $input)
  }
`
