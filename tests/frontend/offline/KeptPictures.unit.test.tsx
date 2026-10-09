import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { deviceData } from '../../../src/frontend/offline/deviceData.ts'
import { memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import { keptPictures, pictureIdOf, usePictureSrc } from '../../../src/frontend/offline/keptPictures.ts'

const KEPT = 'a'.repeat(32)
const OTHER = 'b'.repeat(32)

let created: string[]
let revoked: string[]

beforeEach(async () => {
  created = []
  revoked = []
  URL.createObjectURL = vi.fn(() => {
    const url = `blob:kept-${created.length}`
    created.push(url)
    return url
  })
  URL.revokeObjectURL = vi.fn((url: string) => void revoked.push(url))
  deviceData.reset(memoryStorage())
  await deviceData.signedIn('alice')
  const rows = await deviceData.rows()
  await rows!.putPicture({ id: KEPT, type: 'image/webp', bytes: new Uint8Array([1, 2]).buffer, keptAt: 1 })
})

it('knows a picture by the address the server gives it', () => {
  expect(pictureIdOf(`/media/${KEPT}`)).toBe(KEPT)
  expect(pictureIdOf('blob:preview-1')).toBeNull()
  expect(pictureIdOf(`/media/${KEPT}/x`)).toBeNull()
  expect(pictureIdOf(null)).toBeNull()
})

it('shows a kept picture from the device, one address for every place that shows it', async () => {
  const first = renderHook(() => usePictureSrc(`/media/${KEPT}`))
  expect(first.result.current).toEqual({ src: null, waiting: true }) // not loaded from the server meanwhile

  await waitFor(() => expect(first.result.current).toEqual({ src: 'blob:kept-0', waiting: false }))
  const second = renderHook(() => usePictureSrc(`/media/${KEPT}`))
  expect(second.result.current.src).toBe('blob:kept-0')
  expect(created).toHaveLength(1)
})

it('shows any other picture from the server, as before', async () => {
  const { result } = renderHook(() => usePictureSrc(`/media/${OTHER}`))
  await waitFor(() => expect(result.current).toEqual({ src: `/media/${OTHER}`, waiting: false }))

  expect(renderHook(() => usePictureSrc('blob:preview-1')).result.current).toEqual({ src: 'blob:preview-1', waiting: false })
  expect(renderHook(() => usePictureSrc(null)).result.current).toEqual({ src: null, waiting: false })
})

it('lets go of the pictures when another account, or nobody, is signed in', async () => {
  const { result } = renderHook(() => usePictureSrc(`/media/${KEPT}`))
  await waitFor(() => expect(result.current.src).toBe('blob:kept-0'))

  await act(() => deviceData.signedIn('bob'))

  expect(revoked).toEqual(['blob:kept-0'])
  await waitFor(() => expect(result.current).toEqual({ src: `/media/${KEPT}`, waiting: false })) // bob's data holds no copy
})

it('a picture the download no longer keeps is shown from the server again', async () => {
  const { result } = renderHook(() => usePictureSrc(`/media/${KEPT}`))
  await waitFor(() => expect(result.current.src).toBe('blob:kept-0'))

  await (await deviceData.rows())!.deletePictures([KEPT])
  act(() => keptPictures.refresh())

  await waitFor(() => expect(result.current).toEqual({ src: `/media/${KEPT}`, waiting: false }))
  expect(revoked).toEqual(['blob:kept-0'])
})

it('Remove offline data lets go of the pictures on screen too', async () => {
  const { result } = renderHook(() => usePictureSrc(`/media/${KEPT}`))
  await waitFor(() => expect(result.current.src).toBe('blob:kept-0'))

  await act(() => deviceData.removeAll())

  expect(revoked).toEqual(['blob:kept-0'])
  await waitFor(() => expect(result.current).toEqual({ src: `/media/${KEPT}`, waiting: false }))
  expect(await (await deviceData.rows())!.pictureIds()).toEqual([])
})

it('with no account\'s data open, pictures come from the server', async () => {
  deviceData.reset(memoryStorage()) // nobody signed in on this device yet

  const { result } = renderHook(() => usePictureSrc(`/media/${KEPT}`))

  await waitFor(() => expect(result.current).toEqual({ src: `/media/${KEPT}`, waiting: false }))
})

it('when the device cannot say which pictures it keeps, they come from the server', async () => {
  vi.spyOn((await deviceData.rows())!, 'pictureIds').mockRejectedValue(new Error('broken'))

  const { result } = renderHook(() => usePictureSrc(`/media/${KEPT}`))

  await waitFor(() => expect(result.current).toEqual({ src: `/media/${KEPT}`, waiting: false }))
})

it('a picture listed as kept but gone when read (another tab\'s download removed it) comes from the server', async () => {
  const other = renderHook(() => usePictureSrc(`/media/${OTHER}`)) // reads which pictures are kept
  await waitFor(() => expect(other.result.current.waiting).toBe(false))
  await (await deviceData.rows())!.deletePictures([KEPT])

  const { result } = renderHook(() => usePictureSrc(`/media/${KEPT}`))

  await waitFor(() => expect(result.current).toEqual({ src: `/media/${KEPT}`, waiting: false }))
  expect(created).toEqual([])
})

it('a picture that cannot be read comes from the server', async () => {
  vi.spyOn((await deviceData.rows())!, 'picture').mockRejectedValue(new Error('broken'))

  const { result } = renderHook(() => usePictureSrc(`/media/${KEPT}`))

  await waitFor(() => expect(result.current).toEqual({ src: `/media/${KEPT}`, waiting: false }))
  expect(created).toEqual([])
})

it('a picture still being read when another account signs in is never shown', async () => {
  let finish!: () => void
  const rows = (await deviceData.rows())!
  const stored = await rows.picture(KEPT)
  vi.spyOn(rows, 'picture').mockImplementation(() => new Promise((resolve) => (finish = () => resolve(stored))))
  const { result } = renderHook(() => usePictureSrc(`/media/${KEPT}`))
  await waitFor(() => expect(rows.picture).toHaveBeenCalled())

  await act(() => deviceData.signedIn('bob'))
  await act(async () => finish())

  await waitFor(() => expect(result.current).toEqual({ src: `/media/${KEPT}`, waiting: false })) // bob keeps no copy
  expect(created).toEqual([])
})

it('a download that removes one picture leaves the others on screen as they are', async () => {
  await (await deviceData.rows())!.putPicture({ id: OTHER, type: 'image/webp', bytes: new Uint8Array([3]).buffer, keptAt: 1 })
  const kept = renderHook(() => usePictureSrc(`/media/${KEPT}`))
  const other = renderHook(() => usePictureSrc(`/media/${OTHER}`))
  await waitFor(() => expect([kept.result.current.src, other.result.current.src].sort()).toEqual(['blob:kept-0', 'blob:kept-1']))
  const otherUrl = other.result.current.src

  await (await deviceData.rows())!.deletePictures([KEPT])
  act(() => keptPictures.refresh())

  await waitFor(() => expect(kept.result.current.src).toBe(`/media/${KEPT}`))
  expect(other.result.current.src).toBe(otherUrl)
  expect(revoked).not.toContain(otherUrl)
})

it('a read that fails after another account signed in changes nothing for the new one', async () => {
  let fail!: () => void
  const rows = (await deviceData.rows())!
  vi.spyOn(rows, 'picture').mockImplementation(() => new Promise((_, reject) => (fail = () => reject(new Error('broken')))))
  const { result } = renderHook(() => usePictureSrc(`/media/${KEPT}`))
  await waitFor(() => expect(rows.picture).toHaveBeenCalled())

  await act(() => deviceData.signedIn('bob'))
  await (await deviceData.rows())!.putPicture({ id: KEPT, type: 'image/webp', bytes: new Uint8Array([4]).buffer, keptAt: 1 }) // bob keeps it too
  act(() => keptPictures.refresh())
  await act(async () => fail())

  await waitFor(() => expect(result.current.src).toMatch(/^blob:kept-/)) // alice's failed read did not mark it missing for bob
})
