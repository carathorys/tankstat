import { ApiError } from './ApiError.ts'

async function check(response: Response): Promise<Response> {
  if (response.ok) return response
  let body: { key?: string; args?: Record<string, unknown>; message?: string } = {}
  try {
    body = (await response.json()) as typeof body
  } catch {
    // not JSON (e.g. a proxy error page)
  }
  throw new ApiError(body.key, body.args ?? {}, body.message ?? response.statusText, response.status)
}

/** Sends a picture as the raw request body (the server checks what it really is). Returns the new picture's address. */
export async function uploadImage(path: string, image: Blob): Promise<{ id: string; url: string }> {
  const response = await check(await fetch(path, { method: 'PUT', body: image, credentials: 'same-origin' }))
  return (await response.json()) as { id: string; url: string }
}

export async function deleteImage(path: string): Promise<void> {
  await check(await fetch(path, { method: 'DELETE', credentials: 'same-origin' }))
}

export const avatarPath = '/media/me/avatar'
export const vehiclePicturePath = (vehicleId: string) => `/media/vehicles/${vehicleId}/picture`

/** The logs that can have photos (the path segment of their endpoints). */
export type LogKind = 'expenses' | 'refuelings'

/** At most this many photos per log (the server enforces it too) and the longest edge they are scaled down to in the browser. */
export const MAX_LOG_PHOTOS = 10
export const LOG_PHOTO_EDGE = 1600

export const logPhotosPath = (kind: LogKind, logId: string) => `/media/${kind}/${logId}/photos`
export const logPhotoPath = (kind: LogKind, logId: string, imageId: string) => `${logPhotosPath(kind, logId)}/${imageId}`
