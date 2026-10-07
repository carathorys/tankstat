/**
 * A random (version 4) UUID. Not `crypto.randomUUID()`: it only exists in secure contexts, and the app is also served over plain HTTP;
 * `crypto.getRandomValues` is available everywhere. `random` is for tests.
 */
export function uuidV4(random: (length: number) => Uint8Array = (length) => crypto.getRandomValues(new Uint8Array(length))): string {
  const bytes = random(16)
  bytes[6] = (bytes[6] & 0x0f) | 0x40 // version 4
  bytes[8] = (bytes[8] & 0x3f) | 0x80 // the RFC 4122 variant
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}
