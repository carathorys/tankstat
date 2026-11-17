import { fetchSignedIn } from '../../auth/refresh.ts'
import { navigation } from '../../navigation.ts'
import { check } from '../../pictures/upload.ts'

/** A HEAD answer has no body to carry the key: its status says it. */
const DOWNLOAD_REFUSALS: Partial<Record<number, string>> = { 401: 'auth.unauthenticated', 403: 'export.disabled', 404: 'export.notFound' }

/**
 * Downloads an export (#341): the server is asked first (HEAD, nothing read), so a file that expired or was replaced since the page last
 * asked is said on the page instead of replacing it, as a backup's download does.
 */
export async function downloadExport(url: string): Promise<void> {
  await check(await fetchSignedIn(url, { method: 'HEAD', credentials: 'same-origin' }), DOWNLOAD_REFUSALS)
  navigation.download(url)
}
