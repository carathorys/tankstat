/** A recipient is long; the start and the end tell two apart. */
export const shortRecipient = (recipient: string) => (recipient.length > 20 ? `${recipient.slice(0, 10)}…${recipient.slice(-6)}` : recipient)
