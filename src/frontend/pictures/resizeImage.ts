export interface ResizeOptions {
  /** Longest edge in pixels after resizing. */
  maxEdge: number
  /** Crop to a centred square (profile pictures). */
  square?: boolean
}

export class UnreadableImageError extends Error {
  constructor() {
    super('The file could not be read as a picture.')
    this.name = 'UnreadableImageError'
  }
}

/**
 * Makes a picture small enough to upload before it leaves the browser: decoded, scaled down (never up), optionally cropped to a
 * square and re-encoded as WebP (JPEG where WebP is not available). Re-encoding also drops metadata such as GPS positions.
 */
export async function resizeImage(file: Blob, { maxEdge, square = false }: ResizeOptions): Promise<Blob> {
  let bitmap: ImageBitmap
  try {
    bitmap = await createImageBitmap(file)
  } catch {
    throw new UnreadableImageError()
  }

  try {
    const side = Math.min(bitmap.width, bitmap.height)
    const source = square
      ? { x: (bitmap.width - side) / 2, y: (bitmap.height - side) / 2, width: side, height: side }
      : { x: 0, y: 0, width: bitmap.width, height: bitmap.height }
    const scale = Math.min(1, maxEdge / Math.max(source.width, source.height))
    const width = Math.max(1, Math.round(source.width * scale))
    const height = Math.max(1, Math.round(source.height * scale))

    const canvas = document.createElement('canvas')
    canvas.width = width
    canvas.height = height
    const context = canvas.getContext('2d')
    if (!context) throw new UnreadableImageError()
    context.drawImage(bitmap, source.x, source.y, source.width, source.height, 0, 0, width, height)

    return await encode(canvas)
  } finally {
    bitmap.close()
  }
}

function encode(canvas: HTMLCanvasElement): Promise<Blob> {
  const toBlob = (type: string) => new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, type, 0.85))
  return toBlob('image/webp').then(async (webp) => {
    // Browsers that cannot encode WebP silently answer with PNG; ask for JPEG then.
    if (webp?.type === 'image/webp') return webp
    const jpeg = await toBlob('image/jpeg')
    if (!jpeg) throw new UnreadableImageError()
    return jpeg
  })
}
