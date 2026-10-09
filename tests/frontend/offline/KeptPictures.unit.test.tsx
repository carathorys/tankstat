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
