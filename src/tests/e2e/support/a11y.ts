import AxeBuilder from '@axe-core/playwright'
import { expect, type Page } from '@playwright/test'

/**
 * Scans the current page against WCAG 2.1 A/AA with axe-core and fails on serious or critical violations, listing each with
 * a hint of where it is. (Minor/moderate ones are returned so a test can log them without failing.) Call it once the page
 * has finished loading the content under test: it only sees what is rendered.
 */
export async function expectAccessible(page: Page, what: string) {
  const { violations } = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()

  const blocking = violations
    .filter((v) => v.impact === 'serious' || v.impact === 'critical')
    .map((v) => `${v.id} [${v.impact}]: ${v.help}. ${v.nodes.length} element(s), first: ${v.nodes[0]?.target.join(' ')}`)

  expect(blocking, `accessibility problems on ${what}`).toEqual([])
  return violations.filter((v) => v.impact !== 'serious' && v.impact !== 'critical')
}
