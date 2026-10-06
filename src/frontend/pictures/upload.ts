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

/** With `reading`, a photo added to the saved log is read too (the edit dialog fills in what is still empty). */
export const logPhotosPath = (kind: LogKind, logId: string, reading?: { purpose: ReadingPurpose; locale: string }) =>
  `/media/${kind}/${logId}/photos` + readingQuery(reading)
/** What a photo is picked for: it may show an odometer or a receipt of that kind (the server reads it when photo reading is on). */
export type ReadingPurpose = 'refueling' | 'expense'

/**
 * Photos picked for a log that is not saved yet are uploaded here at once, as drafts that the save then attaches. With `reading`, the
 * server also reads the photo (if that is set up) in the language the user works in.
 */
const readingQuery = (reading?: { purpose: ReadingPurpose; locale: string }) => (reading ? `?form=${reading.purpose}&locale=${encodeURIComponent(reading.locale)}` : '')

export const photoDraftsPath = (vehicleId: string, reading?: { purpose: ReadingPurpose; locale: string }) =>
  `/media/vehicles/${vehicleId}/photo-drafts` + readingQuery(reading)
export const photoDraftPath = (draftId: string) => `/media/photo-drafts/${draftId}`
export const logPhotoPath = (kind: LogKind, logId: string, imageId: string) => `${logPhotosPath(kind, logId)}/${imageId}`
