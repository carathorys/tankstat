export interface ResizeOptions {
  /** Longest edge in pixels after resizing. */
  maxEdge: number
  /** Crop to a centred square (profile pictures). */
  square?: boolean
  /** Encode as JPEG instead of WebP: what every picture reader decodes (a model server may not know WebP), at a somewhat larger size. */
  format?: 'jpeg'
}

export class UnreadableImageError extends Error {
  constructor() {
    super('The file could not be read as a picture.')
    this.name = 'UnreadableImageError'
  }
}

/**
 * Makes a picture small enough to upload before it leaves the browser: decoded, scaled down (never up), optionally cropped to a
 * square and re-encoded as WebP (JPEG where WebP is not available, or when asked for). Re-encoding also drops metadata such as GPS positions.
 */
export async function resizeImage(file: Blob, { maxEdge, square = false, format }: ResizeOptions): Promise<Blob> {
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

    return await encode(canvas, format)
  } finally {
    bitmap.close()
  }
}

function encode(canvas: HTMLCanvasElement, format?: 'jpeg'): Promise<Blob> {
  const toBlob = (type: string) => new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, type, 0.85))
  const jpeg = async () => {
    const blob = await toBlob('image/jpeg')
    if (!blob) throw new UnreadableImageError()
    return blob
  }
  if (format === 'jpeg') return jpeg()
  // Browsers that cannot encode WebP silently answer with PNG; ask for JPEG then.
  return toBlob('image/webp').then((webp) => (webp?.type === 'image/webp' ? webp : jpeg()))
}
