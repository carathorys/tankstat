import { expect, it } from 'vitest'
import { cardClickOrigin, tappedAway } from '../../src/frontend/components/cardClicks.ts'

/** A card with a link, a button (with an icon) and plain text; next to it a dialog (as a portal renders it) and something else. */
function dom() {
  document.body.innerHTML = '<div class="card"><a href="/v">Open</a><button><svg></svg></button><p>Figures</p></div><div role="dialog"><input /></div><h1>Elsewhere</h1>'
  const card = document.querySelector('.card')!
  return { card, svg: card.querySelector('svg')!, linkText: card.querySelector('a')!.firstChild, p: card.querySelector('p')!, input: document.querySelector('input')!, h1: document.querySelector('h1')! }
}

it('tells the card apart from its buttons and links, and from a dialog whose events only bubble through it', () => {
  const d = dom()

  expect(cardClickOrigin(d.card, d.svg)).toBe('button')
  expect(cardClickOrigin(d.card, d.linkText)).toBe('link')
  expect(cardClickOrigin(d.card, d.p)).toBe('card')
  expect(cardClickOrigin(d.card, d.input)).toBe('outside')
  expect(cardClickOrigin(d.card, d.h1)).toBe('outside')
  expect(cardClickOrigin(null, d.p)).toBe('outside')
  expect(cardClickOrigin(d.card, null)).toBe('outside')
})

it('a tap puts the figures away only outside the card and outside an open dialog', () => {
  const d = dom()

  expect(tappedAway(d.card, d.p)).toBe(false)
  expect(tappedAway(d.card, d.input)).toBe(false)
  expect(tappedAway(d.card, d.h1)).toBe(true)
  expect(tappedAway(d.card, null)).toBe(false)
})
