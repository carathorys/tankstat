import { AxeBuilder } from '@axe-core/playwright'
import type { Page } from '@playwright/test'

/** The axe violations of the page, or of the part `include` selects, one readable line each. */
export async function violations(page: Page, include?: string) {
  const builder = new AxeBuilder({ page })
  if (include) builder.include(include)
  const { violations } = await builder.analyze()
  return violations.map((v) => `${v.id} (${v.impact}): ${v.nodes.length} nodes, e.g. ${v.nodes[0]?.target.join(' ')}`)
}
