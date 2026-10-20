import Alert from '@mui/material/Alert'
import { Info } from 'lucide-react'

/**
 * Why the photos gave less than they might have, a notice for each reason, inside the dialog's status region (so a screen reader hears
 * them too). The icon and the words carry it, not the colour.
 */
export function ReadingProblems({ problems }: { problems: readonly string[] }) {
  return problems.map((problem) => (
    <Alert key={problem} severity="warning" role="none" icon={<Info size={16} aria-hidden />} sx={{ my: 1 }}>
      {problem}
    </Alert>
  ))
}
