import { fetchSignedIn, refreshSession } from '../auth/refresh.ts'
import { ApiError } from './ApiError.ts'

/** Sends a file to import as the raw request body; the answer is a token that stands for the parsed file on the server. */
export async function uploadImportFile(format: string, file: Blob): Promise<{ token: string }> {
  const response = await fetchSignedIn(`/imports/${encodeURIComponent(format)}`, { method: 'POST', body: file, credentials: 'same-origin' })
  if (!response.ok) {
    let body: { key?: string; args?: Record<string, unknown>; message?: string } = {}
    try {
      body = (await response.json()) as typeof body
    } catch {
      // not JSON (e.g. a proxy error page)
    }
    throw new ApiError(body.key, body.args ?? {}, body.message ?? response.statusText, response.status)
  }
  return (await response.json()) as { token: string }
}

/**
 * Sends a Tankstat backup or export to import cars from (#336): streamed as the raw body, it can be hundreds of megabytes, so
 * `onProgress` hears the share sent (0..1). The access cookie is renewed first, so the file is not sent twice when the cookie ran
 * out. The answer is the file's token; how its opening stands is asked through GraphQL.
 */
export async function uploadBackupImport(file: Blob, onProgress?: (share: number) => void): Promise<{ token: string }> {
  await refreshSession().catch(() => false)
  return new Promise((resolve, reject) => {
    const request = new XMLHttpRequest()
    request.open('POST', '/imports/tankstat')
    request.withCredentials = true
    request.setRequestHeader('Content-Type', 'application/zip')
    request.upload.onprogress = (e) => {
      if (e.lengthComputable && e.total > 0) onProgress?.(e.loaded / e.total)
    }
    request.onerror = () => reject(new TypeError('Failed to fetch')) // nothing answered: the page's network words
    request.onload = () => {
      let body: { token?: string; key?: string; args?: Record<string, unknown>; message?: string } = {}
      try {
        body = JSON.parse(request.responseText) as typeof body
      } catch {
        // not JSON (a proxy's error page)
      }
      if (request.status >= 200 && request.status < 300 && body.token) resolve({ token: body.token })
      else reject(new ApiError(body.key, body.args ?? {}, body.message ?? request.statusText, request.status))
    }
    request.send(file)
  })
}
