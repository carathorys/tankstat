import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { deviceData } from '../../../src/frontend/offline/deviceData.ts'
import { memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import { DRAFT_REUSE_MS, GC_GRACE_MS, keptPhotos } from '../../../src/frontend/offline/keptPhotos.ts'
import { uploadImage } from '../../../src/frontend/pictures/upload.ts'

vi.mock('../../../src/frontend/pictures/upload.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../src/frontend/pictures/upload.ts')>()),
  uploadImage: vi.fn(),
}))

const picture = () => new Blob([new Uint8Array([1, 2, 3])], { type: 'image/jpeg' })

let n = 0
beforeEach(async () => {
  URL.createObjectURL = vi.fn(() => `blob:kept-${n++}`)
  URL.revokeObjectURL = vi.fn()
  vi.mocked(uploadImage).mockReset()
  deviceData.reset(memoryStorage())
  await deviceData.signedIn('alice')
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('keptPhotos', () => {
  it('keeps nothing when no account\'s data is open', async () => {
    deviceData.reset()

    expect(await keptPhotos.keep(picture(), 'v1')).toBeNull()
    expect(await keptPhotos.get('local:anything')).toBeUndefined()
  })

  it('shows a kept photo at the same address every time, and nothing for one the device does not have', async () => {
    const key = (await keptPhotos.keep(picture(), 'v1'))!

    const url = await keptPhotos.url(key)
    expect(url).toMatch(/^blob:kept-/)
    expect(await keptPhotos.url(key)).toBe(url)
    expect(URL.createObjectURL).toHaveBeenCalledTimes(1)
    expect(await keptPhotos.url('local:never-kept')).toBeUndefined()

    await keptPhotos.remove([key])
    expect(URL.revokeObjectURL).toHaveBeenCalledWith(url)
    expect(await keptPhotos.get(key)).toBeUndefined()
  })

  it('uploads a kept photo as a draft to be read, and uses that draft again until it may have expired', async () => {
    vi.mocked(uploadImage).mockResolvedValueOnce({ id: 'd1', url: '/media/d1' }).mockResolvedValueOnce({ id: 'd2', url: '/media/d2' })
    const key = (await keptPhotos.keep(picture(), 'v1', { purpose: 'refueling', locale: 'hu' }))!
    const now = 1_000_000

    expect(await keptPhotos.asDraft(key, now)).toBe('d1')
    expect(vi.mocked(uploadImage).mock.calls[0][0]).toBe('/media/vehicles/v1/photo-drafts?form=refueling&locale=hu')
    expect(await keptPhotos.asDraft(key, now + DRAFT_REUSE_MS - 1)).toBe('d1') // a try that failed after uploading leaves no second draft
    expect(uploadImage).toHaveBeenCalledTimes(1)

    expect(await keptPhotos.asDraft(key, now + DRAFT_REUSE_MS)).toBe('d2')
    expect(uploadImage).toHaveBeenCalledTimes(2)

    expect(await keptPhotos.asDraft('local:gone', now)).toBeUndefined()
  })

  it('removes the photos no change refers to once they are old enough, and does nothing without an account\'s data', async () => {
    vi.spyOn(Date, 'now').mockReturnValue(1_000)
    const orphan = (await keptPhotos.keep(picture(), 'v1'))!
    const live = (await keptPhotos.keep(picture(), 'v1'))!
    vi.restoreAllMocks()

    await keptPhotos.gc(new Set([live]), 1_000 + GC_GRACE_MS) // not older than the grace yet
    expect(await keptPhotos.get(orphan)).toBeDefined()

    await keptPhotos.gc(new Set([live]), 1_001 + GC_GRACE_MS)
    expect(await keptPhotos.get(orphan)).toBeUndefined()
    expect(await keptPhotos.get(live)).toBeDefined()

    deviceData.reset()
    await expect(keptPhotos.gc(new Set(), Number.MAX_SAFE_INTEGER)).resolves.toBeUndefined()
  })
})
