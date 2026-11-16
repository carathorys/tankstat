import { test } from '@playwright/test'

export type WaitingServer = { url: string; code: string }

/**
 * The new instance this test takes over, waiting for its setup, and its one-time code: scripts/test-e2e.sh starts one per setup test and
 * size (`E2E_SETUP`, by `<project>-<kind>`), since finishing a setup or restoring into it cannot be undone. `fresh` runs the shipped
 * sign-in off, `restore` Standalone without an administrator, `reopened` an instance set up with sign-in off and started again in
 * Standalone, whose setup reopened since it has no administrator (#149).
 */
export function waitingServer(kind: 'fresh' | 'restore' | 'reopened'): WaitingServer {
  const name = `${test.info().project.name}-${kind}`
  const server = (JSON.parse(process.env.E2E_SETUP ?? '{}') as Record<string, WaitingServer>)[name]
  if (!server) throw new Error(`No waiting server "${name}" in E2E_SETUP: scripts/test-e2e.sh starts them.`)
  return server
}

/**
 * The server this size's import-from-a-backup tests have to themselves (sign-in off), and the backup file they import from:
 * scripts/test-e2e.sh starts one per size and writes the file (`E2E_IMPORT`). A run of an import holds SQLite's one lock to write, so on
 * the shared server every other test's saves would wait for it.
 */
export function importServer(): { url: string; backup: string } {
  const all = JSON.parse(process.env.E2E_IMPORT ?? '{}') as Record<string, { url: string }> & { backup?: string }
  const server = all[test.info().project.name]
  if (!server || typeof all.backup !== 'string') throw new Error('No import server in E2E_IMPORT: scripts/test-e2e.sh starts them.')
  return { url: server.url, backup: all.backup }
}
