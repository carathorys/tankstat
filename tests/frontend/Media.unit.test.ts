import { afterEach, expect, it, vi } from 'vitest'
import { ApiError } from '../../src/frontend/pictures/ApiError.ts'
import { resizeImage, UnreadableImageError } from '../../src/frontend/pictures/resizeImage.ts'
import { avatarPath, deleteImage, LOG_PHOTO_EDGE, logPhotoPath, logPhotosPath, MAX_LOG_PHOTOS, photoDraftPath, photoDraftsPath, uploadImage, vehiclePicturePath } from '../../src/frontend/pictures/upload.ts'

afterEach(() => vi.unstubAllGlobals())

const png = new Blob([new Uint8Array([1, 2, 3])], { type: 'image/png' })

function stubCanvas(width: number, height: number, answers: (type: string) => Blob | null) {
  const close = vi.fn()
  const drawImage = vi.fn()
  vi.stubGlobal('createImageBitmap', vi.fn(async () => ({ width, height, close })))
  const canvas = { width: 0, height: 0, getContext: () => ({ drawImage }), toBlob: (cb: (b: Blob | null) => void, type: string) => cb(answers(type)) }
  vi.spyOn(document, 'createElement').mockReturnValue(canvas as unknown as HTMLCanvasElement)
  return { canvas, drawImage, close }
}

it('scales a large picture down to the longest edge, keeping the proportions, and never scales up', async () => {
  const big = stubCanvas(4000, 3000, () => new Blob(['w'], { type: 'image/webp' }))
  await resizeImage(png, { maxEdge: 1000 })
  expect([big.canvas.width, big.canvas.height]).toEqual([1000, 750])
  expect(big.close).toHaveBeenCalled()

  const small = stubCanvas(200, 100, () => new Blob(['w'], { type: 'image/webp' }))
  await resizeImage(png, { maxEdge: 1000 })
  expect([small.canvas.width, small.canvas.height]).toEqual([200, 100])
})

it('crops a centred square for profile pictures', async () => {
  const s = stubCanvas(800, 400, () => new Blob(['w'], { type: 'image/webp' }))

  await resizeImage(png, { maxEdge: 256, square: true })

  expect([s.canvas.width, s.canvas.height]).toEqual([256, 256])
  expect(s.drawImage).toHaveBeenCalledWith(expect.anything(), 200, 0, 400, 400, 0, 0, 256, 256)
})

it('encodes as WebP, or JPEG where the browser cannot make WebP', async () => {
  stubCanvas(10, 10, (type) => new Blob(['x'], { type }))
  expect((await resizeImage(png, { maxEdge: 100 })).type).toBe('image/webp')

  stubCanvas(10, 10, (type) => new Blob(['x'], { type: type === 'image/webp' ? 'image/png' : type })) // unsupported: answers PNG
  expect((await resizeImage(png, { maxEdge: 100 })).type).toBe('image/jpeg')
})

it('rejects a file the browser cannot decode', async () => {
  vi.stubGlobal('createImageBitmap', vi.fn(async () => Promise.reject(new Error('bad'))))

  await expect(resizeImage(png, { maxEdge: 100 })).rejects.toBeInstanceOf(UnreadableImageError)
})

it('uploads the picture as the raw request body and returns its address', async () => {
  const fetchMock = vi.fn(async () => new Response(JSON.stringify({ id: 'i1', url: '/media/i1' })))
  vi.stubGlobal('fetch', fetchMock)

  expect(await uploadImage(avatarPath, png)).toEqual({ id: 'i1', url: '/media/i1' })
  expect(fetchMock).toHaveBeenCalledWith('/media/me/avatar', { method: 'PUT', body: png, credentials: 'same-origin' })
  expect(vehiclePicturePath('abc')).toBe('/media/vehicles/abc/picture')
})

it('turns an error answer into an ApiError with the translation key and arguments', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ key: 'image.tooLarge', args: { maxKb: 2048 }, message: 'too big' }), { status: 413 })))

  const error = await uploadImage(avatarPath, png).catch((e: unknown) => e)

  expect(error).toBeInstanceOf(ApiError)
  expect(error).toMatchObject({ key: 'image.tooLarge', args: { maxKb: 2048 }, status: 413, message: 'too big' })
})

it('copes with an error answer that is not JSON, and deletes pictures', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response('<html>bad gateway</html>', { status: 502, statusText: 'Bad Gateway' })))
  await expect(deleteImage(avatarPath)).rejects.toMatchObject({ key: undefined, status: 502, message: 'Bad Gateway' })

  const ok = vi.fn(async () => new Response(null, { status: 204 }))
  vi.stubGlobal('fetch', ok)
  await deleteImage(vehiclePicturePath('v1'))
  expect(ok).toHaveBeenCalledWith('/media/vehicles/v1/picture', { method: 'DELETE', credentials: 'same-origin' })
})

it('builds the endpoints of log photos and keeps the limits the server enforces', () => {
  expect(logPhotosPath('expenses', 'e1')).toBe('/media/expenses/e1/photos')
  expect(logPhotosPath('refuelings', 'r1')).toBe('/media/refuelings/r1/photos')
  expect(logPhotoPath('expenses', 'e1', 'img9')).toBe('/media/expenses/e1/photos/img9')
  expect(photoDraftsPath('v1')).toBe('/media/vehicles/v1/photo-drafts') // photos of a log that is not saved yet
  expect(photoDraftPath('d1')).toBe('/media/photo-drafts/d1')
  expect([MAX_LOG_PHOTOS, LOG_PHOTO_EDGE]).toEqual([10, 1600])
})
