/**
 * Where a click on a vehicle card landed. A dialog opened from the card renders in a portal outside the card's DOM, yet React still bubbles
 * its events through the card: those count as 'outside' and the card must leave them alone.
 */
export type CardClickOrigin = 'outside' | 'button' | 'link' | 'card'

/** The element a click hit (a text node's parent when the target is text). */
const elementOf = (target: Node) => (target instanceof Element ? target : target.parentElement)

export function cardClickOrigin(card: Element | null, target: EventTarget | null): CardClickOrigin {
  if (!card || !(target instanceof Node) || !card.contains(target)) return 'outside'
  const element = elementOf(target)
  if (element?.closest('button')) return 'button'
  if (element?.closest('a')) return 'link'
  return 'card'
}

/**
 * What opened from a card and floats outside it: a dialog (with its backdrop) and the lists that open from its fields (a select's options, a
 * date picker, suggestions), each in a portal of its own.
 */
const OPENED_FROM_CARD = '[role="dialog"], [role="alertdialog"], .MuiModal-root, .MuiPopper-root'

/** A pointer went down somewhere that should put a revealed card's figures away: outside the card and not in anything opened from it. */
export function tappedAway(card: Element | null, target: EventTarget | null): boolean {
  if (!(target instanceof Node) || card?.contains(target)) return false
  return !elementOf(target)?.closest(OPENED_FROM_CARD)
}
