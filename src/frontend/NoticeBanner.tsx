import type { Notice } from './session.ts'

/** Shows every notice the server sent, e.g. the "authentication is disabled" warning. */
export function NoticeBanner({ notices }: { notices: Notice[] }) {
  if (notices.length === 0) return null

  return (
    <aside aria-label="Notices">
      {notices.map((n) => (
        <p key={n.code} role="note" data-severity={n.severity} className={`notice notice-${n.severity.toLowerCase()}`}>
          {n.severity === 'WARNING' && <strong>Warning: </strong>}
          {n.message}
        </p>
      ))}
    </aside>
  )
}
