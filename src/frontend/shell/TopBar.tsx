import { Link } from 'react-router'
import type { AuthMode, SessionUser } from '../session.ts'
import { HamburgerMenu } from './HamburgerMenu.tsx'

/** Shown on every screen; the menu appears once the visitor may use the app (signed in, or auth is off). */
export function TopBar({ mode, user, showMenu }: { mode: AuthMode; user: SessionUser | null; showMenu: boolean }) {
  return (
    <header className="topbar">
      {showMenu && <HamburgerMenu mode={mode} user={user} />}
      <Link to="/" className="brand">
        Tankstat
      </Link>
      <span className="topbar-spacer" />
      {user && (
        <span className="topbar-user">
          {user.displayName}
          {user.isAdmin && <span className="badge">admin</span>}
        </span>
      )}
    </header>
  )
}
