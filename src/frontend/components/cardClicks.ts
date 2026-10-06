/**
 * Where a click on a vehicle card landed. A dialog opened from the card renders in a portal outside the card's DOM, yet React still bubbles
 * its events through the card: those count as 'outside' and the card must leave them alone.
 */
export type CardClickOrigin = 'outside' | 'button' | 'link' | 'card'

export function cardClickOrigin(card: Element | null, target: EventTarget | null): CardClickOrigin {
  if (!card || !(target instanceof Node) || !card.contains(target)) return 'outside'
  const element = target instanceof Element ? target : target.parentElement
  if (element?.closest('button')) return 'button'
  if (element?.closest('a')) return 'link'
  return 'card'
}

/** A pointer went down somewhere that should put a revealed card's figures away: outside the card and not inside an open dialog. */
export function tappedAway(card: Element | null, target: EventTarget | null): boolean {
  if (!(target instanceof Node) || card?.contains(target)) return false
  const element = target instanceof Element ? target : target.parentElement
  return !element?.closest('[role="dialog"]')
}
