import { useQuery } from '@apollo/client/react'
import { HEALTH_QUERY } from '../health.ts'
import { StatusBadge } from '../StatusBadge.tsx'

export function HealthFooter() {
  const { data, error } = useQuery(HEALTH_QUERY)

  return (
    <footer className="footer">
      {error && <span role="alert">API unreachable: {error.message}</span>}
      {data && (
        <span>
          API: <StatusBadge status={data.health.status} /> (v{data.health.version})
        </span>
      )}
    </footer>
  )
}
