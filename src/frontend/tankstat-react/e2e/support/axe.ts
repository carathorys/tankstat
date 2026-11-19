import { AxeBuilder } from '@axe-core/playwright'
import { expect, type Page } from '@playwright/test'

/**
 * The axe violations of the page, or of the part `include` selects, one readable line each. A part is scanned once it is fully drawn
 * and nothing in it moves any more (#235, #319):
 * - **Faded in:** every element `include` selects and its ancestors at full opacity. axe calls a node under a fading ancestor
 *   "incomplete" and passes it, so a scan of a dialog mid fade-in would pass silently.
 * - **Still:** no finite CSS animation or transition running inside the scanned part (the whole document without `include`) or on one
 *   of its ancestors. A fading descendant is measured, not passed: the Add widget dialog's period panel (`tk-fade`) read its text at
 *   4.43:1 when the scan came in its first 65 ms. Animations that never end (`tk-pulse`, `tk-spin`, a skeleton's pulse) are not waited
 *   for, nor is motion driven by script (motion/react, react-grid-layout: `settled` in `app.ts` is for those).
 * Both are polled for up to 5 s; past it the test fails naming what did not match or what kept moving.
 */
export async function violations(page: Page, include?: string) {
  await expect.poll(() => page.evaluate(unsettled, include ?? null), { timeout: 5_000, message: `the part to scan (${include ?? 'the page'}) is drawn and still` }).toEqual([])
  const builder = new AxeBuilder({ page })
  if (include) builder.include(include)
  const { violations } = await builder.analyze()
  // Every node named, with the start of its markup, so a failure says what is wrong without a report.
  // A contrast failure names its colours and ratio too (`data` of the check), so it can be judged from the line.
  return violations.map((v) => `${v.id} (${v.impact}): ${v.nodes.map((n) => `${n.target.join(' ')} <${n.html.slice(0, 120)}> ${n.any[0]?.data ? JSON.stringify(n.any[0].data) : ''}`).join('; ')}`)
}

/** Runs in the page: what keeps the part from being scanned yet, one line each; empty once it is drawn and still. */
function unsettled(selector: string | null): string[] {
  const name = (el: Element) => `<${el.tagName.toLowerCase()}${el.getAttribute('role') ? ` role="${el.getAttribute('role')}"` : ''}${el.className && typeof el.className === 'string' ? ` class="${el.className.slice(0, 60)}"` : ''}>`
  const parts = selector ? [...document.querySelectorAll(selector)] : [document.documentElement]
  if (parts.length === 0) return [`nothing matches ${selector}`] // named here rather than at the test's timeout
  const problems: string[] = []
  if (selector)
    for (const part of parts)
      for (let node: Element | null = part; node; node = node.parentElement)
        if (getComputedStyle(node).opacity !== '1') { problems.push(`fading in: ${name(node)} at opacity ${getComputedStyle(node).opacity}`); break }
  for (const animation of document.getAnimations()) {
    // Finished ones stay listed, and those that never end would hold the scan forever: only a finite one that is running counts.
    if (animation.playState !== 'running') continue
    const timing = animation.effect?.getComputedTiming()
    if (!timing || timing.iterations === Infinity || timing.endTime === Infinity) continue
    const target = (animation.effect as KeyframeEffect | null)?.target
    if (!target || !parts.some((part) => part.contains(target) || target.contains(part))) continue
    const what = 'animationName' in animation ? `animation ${(animation as CSSAnimation).animationName}`
      : 'transitionProperty' in animation ? `transition of ${(animation as CSSTransition).transitionProperty}` : 'animation'
    problems.push(`moving: ${what} on ${name(target)}`)
  }
  return problems
}
