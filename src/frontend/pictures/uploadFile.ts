import { ApiError } from './ApiError.ts'

/** Sends a file to import as the raw request body; the answer is a token that stands for the parsed file on the server. */
export async function uploadImportFile(format: string, file: Blob): Promise<{ token: string }> {
  const response = await fetch(`/imports/${encodeURIComponent(format)}`, { method: 'POST', body: file, credentials: 'same-origin' })
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
