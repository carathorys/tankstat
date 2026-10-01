import { useApolloClient, useMutation } from '@apollo/client/react'
import { DropdownMenu } from 'radix-ui'
import { Link } from 'react-router'
import { LOGOUT_MUTATION, type AuthMode, type SessionUser } from '../session.ts'

export function HamburgerMenu({ mode, user }: { mode: AuthMode; user: SessionUser | null }) {
  const client = useApolloClient()
  const [logout] = useMutation(LOGOUT_MUTATION)
  const canSignOut = user !== null && mode !== 'PROXY_HEADER' // behind a proxy the proxy owns the session

  return (
    <DropdownMenu.Root>
      <DropdownMenu.Trigger asChild>
        <button type="button" className="hamburger" aria-label="Open menu">
          ☰
        </button>
      </DropdownMenu.Trigger>
      <DropdownMenu.Portal>
        <DropdownMenu.Content className="menu" align="start" sideOffset={6}>
          <DropdownMenu.Item asChild className="menu-item">
            <Link to="/vehicles">Vehicles</Link>
          </DropdownMenu.Item>
          <DropdownMenu.Item asChild className="menu-item">
            <Link to="/trash">Trash</Link>
          </DropdownMenu.Item>
          {user && (
            <DropdownMenu.Item asChild className="menu-item">
              <Link to="/account">Account</Link>
            </DropdownMenu.Item>
          )}
          {user?.isAdmin && (
            <DropdownMenu.Item asChild className="menu-item">
              <Link to="/admin">Administration</Link>
            </DropdownMenu.Item>
          )}
          {canSignOut && (
            <>
              <DropdownMenu.Separator className="menu-separator" />
              <DropdownMenu.Item
                className="menu-item"
                onSelect={async () => {
                  await logout()
                  await client.resetStore()
                }}
              >
                Sign out
              </DropdownMenu.Item>
            </>
          )}
        </DropdownMenu.Content>
      </DropdownMenu.Portal>
    </DropdownMenu.Root>
  )
}
