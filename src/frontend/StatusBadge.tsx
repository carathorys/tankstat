export function StatusBadge({ status }: { status: string }) {
  return <span role="status" data-status={status}>{status === 'ok' ? 'Healthy' : 'Degraded'}</span>
}
