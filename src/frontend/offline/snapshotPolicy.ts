/**
 * What the device does with each query while the server is out of reach:
 * - `keep`: its last answer (per variables) is kept and shown again;
 * - `onlineOnly`: a screen that only makes sense with the server (administration, sharing, importing, the devices list): never kept, it
 *   says it needs a connection;
 * - `never`: never kept and never answered from the device: the server's health (a stale "healthy" would hide that it is down) and the
 *   photo readings a dialog polls (a stale "still reading" would wait forever).
 * Every query is listed (a unit test checks the generated documents), so a new one needs a decision.
 */
export type SnapshotPolicy = 'keep' | 'onlineOnly' | 'never'

const POLICIES: Record<string, SnapshotPolicy> = {
  Session: 'keep',
  UiSettings: 'keep',
  VehicleDefaults: 'keep',
  RecognitionStatus: 'keep',
  Welcome: 'keep',
  VehicleCard: 'keep',
  VehicleDetails: 'keep',
  VehicleDashboard: 'keep',
  ChartData: 'keep',
  RecurringExpenses: 'keep',
  Refuelings: 'keep',
  RefuelingDetails: 'keep',
  RefuelingTrash: 'keep',
  LogDefaults: 'keep',
  Expenses: 'keep',
  ExpenseDetails: 'keep',
  ExpenseTrash: 'keep',
  ExpenseCategories: 'keep',
  UnreadNotificationCount: 'keep',
  LatestNotifications: 'keep',
  Notifications: 'keep',
  OfflineSettings: 'keep',
  Admin: 'onlineOnly',
  Vehicles: 'onlineOnly',
  Trash: 'onlineOnly',
  ArrangeVehicles: 'onlineOnly',
  LogAccess: 'onlineOnly',
  ImportTargets: 'onlineOnly',
  ImportPreview: 'onlineOnly',
  MySessions: 'onlineOnly',
  Health: 'never',
  PhotoDraftReadings: 'never',
  RefuelingPhotoReadings: 'never',
  ExpensePhotoReadings: 'never',
  OfflineChanges: 'never', // the download itself: what it brings is kept row by row (pull.ts)
}

export const KNOWN_QUERIES: readonly string[] = Object.keys(POLICIES)

/** Unknown operations are not kept: nothing ends up on the device without a decision. */
export const policyFor = (operationName: string | undefined): SnapshotPolicy => POLICIES[operationName ?? ''] ?? 'never'

/** The operation and its variables, the variables' keys sorted at every level, so the same query always finds its answer. */
export function snapshotKey(operationName: string, variables: Record<string, unknown> | undefined): string {
  return `${operationName}:${stableStringify(variables ?? {})}`
}

function stableStringify(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(stableStringify).join(',')}]`
  if (value !== null && typeof value === 'object') {
    const entries = Object.entries(value as Record<string, unknown>).filter(([, v]) => v !== undefined)
    return `{${entries.sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)).map(([k, v]) => `${JSON.stringify(k)}:${stableStringify(v)}`).join(',')}}`
  }
  return JSON.stringify(value) ?? 'null'
}
