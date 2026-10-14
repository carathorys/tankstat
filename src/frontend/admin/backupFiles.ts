import { fetchSignedIn, refreshSession } from '../auth/refresh.ts'
import { navigation } from '../navigation.ts'
import { check } from '../pictures/upload.ts'

export const backupPath = (file: string) => `/media/backups/${encodeURIComponent(file)}`

/**
 * Sends a backup file into the server's backups folder, streamed as the raw body (it can be a gigabyte). The access cookie is renewed
 * first, so the file is not sent twice when the cookie ran out; with the setup's one-time `code` (in a header, never the address, which
 * proxies log) nobody is signed in, and nothing is renewed. The server checks it is a backup and names it `uploaded-...`.
 */
export async function uploadBackup(file: File, code?: string): Promise<{ file: string; bytes: number }> {
  if (code === undefined) await refreshSession().catch(() => false)
  const response = await fetchSignedIn(`/media/backups?name=${encodeURIComponent(file.name)}`, {
    method: 'PUT',
    body: file,
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/zip', ...(code === undefined ? {} : { 'X-Setup-Code': code }) },
  })
  // A refusal without a key is a proxy in front of the server answering (its own size limit, say).
  await check(response, { 413: 'backup.uploadRefused' })
  return (await response.json()) as { file: string; bytes: number }
}

/** A HEAD answer has no body to carry the key: its status says it. */
const DOWNLOAD_REFUSALS: Partial<Record<number, string>> = { 401: 'auth.unauthenticated', 403: 'auth.adminRequired', 404: 'backup.notFound' }

/**
 * Downloads a backup: the browser saves what the server streams. The server is asked first (HEAD: nothing is read or logged, and an
 * access cookie that ran out is renewed on the way), so a refusal, a file gone since the list or a server out of reach is said on the page.
 * A plain link would put the server's answer in the page's place.
 */
export async function downloadBackup(file: string): Promise<void> {
  const path = backupPath(file)
  await check(await fetchSignedIn(path, { method: 'HEAD', credentials: 'same-origin' }), DOWNLOAD_REFUSALS)
  navigation.download(path)
}
