import { useQuery } from '@apollo/client/react'
import { HEALTH_QUERY } from './health.ts'
import { StatusBadge } from './StatusBadge.tsx'

export default function App() {
  const { data, error, loading } = useQuery(HEALTH_QUERY)

  return (
    <main>
      <h1>Tankstat</h1>
      {error && <p role="alert">{error.message}</p>}
      {loading && <p>Loading…</p>}
      {data && (
        <p>
          API: <StatusBadge status={data.health.status} /> (v{data.health.version})
        </p>
      )}
    </main>
  )
}
