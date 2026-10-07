import { within } from '@testing-library/react'
import type { UserEvent } from '@testing-library/user-event'

/** The date field labelled `label`: the group of its typed sections, once its picker has loaded (it is lazy). */
export const findDateField = (label: string | RegExp, scope: HTMLElement = document.body) => within(scope).findByRole('group', { name: label })

/** What a date field holds as the form sends it: YYYY-MM-DD, or '' while it is empty or only partly typed. */
export function dateValue(field: HTMLElement): string {
  const box = field.closest('[data-date-field]')
  return box?.querySelector<HTMLInputElement>('input[name]')?.value ?? ''
}

/** Types a day (YYYY-MM-DD) into a date field, replacing what it held, section by section in the field's own order (month first in English). */
export async function typeDate(ui: UserEvent, field: HTMLElement, iso: string) {
  const [year, month, day] = iso.split('-')
  const sections = within(field).getAllByRole('spinbutton')
  const digits = sections.map((s) => (s.getAttribute('aria-valuemax') === '12' ? month : s.getAttribute('aria-valuemax') === '31' ? day : year)).join('')
  await ui.click(sections[0])
  await ui.keyboard('{Control>}a{/Control}{Backspace}')
  await ui.keyboard(digits)
}
