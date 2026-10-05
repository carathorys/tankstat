import { Callout } from '@radix-ui/themes'
import { Info } from 'lucide-react'

/**
 * Why the photos gave less than they might have, a notice for each reason, inside the dialog's status region (so a screen reader hears
 * them too). The icon and the words carry it, not the colour.
 */
export function ReadingProblems({ problems }: { problems: readonly string[] }) {
  return problems.map((problem) => (
    <Callout.Root key={problem} color="amber" size="1" my="2">
      <Callout.Icon>
        <Info size={16} aria-hidden />
      </Callout.Icon>
      <Callout.Text>{problem}</Callout.Text>
    </Callout.Root>
  ))
}
