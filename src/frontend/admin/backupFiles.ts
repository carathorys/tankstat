import { fetchSignedIn, refreshSession } from '../auth/refresh.ts'
import { navigation } from '../navigation.ts'
import { ApiError } from '../pictures/ApiError.ts'

export const backupPath = (file: string) => `/media/backups/${encodeURIComponent(file)}`

/**
 * Sends a backup file into the server's backups folder, streamed as the raw body (it can be a gigabyte). The access cookie is renewed
 * first, so the file is not sent twice when the cookie ran out. The server checks it is a backup and names it `uploaded-...`.
 */
export async function uploadBackup(file: File): Promise<{ file: string; bytes: number }> {
  await refreshSession().catch(() => false)
  const response = await fetchSignedIn(`/media/backups?name=${encodeURIComponent(file.name)}`, {
    method: 'PUT',
    body: file,
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/zip' },
  })
  if (!response.ok) {
    let body: { key?: string; args?: Record<string, unknown>; message?: string } = {}
    try {
      body = (await response.json()) as typeof body
    } catch {
      // not JSON: a proxy in front of the server answered (its own size limit, say)
    }
    throw new ApiError(body.key ?? (response.status === 413 ? 'backup.uploadRefused' : undefined), body.args ?? {}, body.message ?? response.statusText, response.status)
  }
  return (await response.json()) as { file: string; bytes: number }
}

/**
 * Downloads a backup: the browser saves what the server streams. A plain link would meet a refusal once the short access cookie ran out
 * (the browser shows that as a page), so the cookie is renewed first.
 */
export async function downloadBackup(file: string): Promise<void> {
  await refreshSession().catch(() => false)
  navigation.download(backupPath(file))
}
