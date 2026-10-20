import { CalendarDays, ChevronDown } from 'lucide-react'

// The app's own icons (lucide) inside MUI's controls, at the size of the text they sit next to.

/** The arrow of a select; MUI positions it and turns it while the list is open. */
export function SelectChevron({ className }: { className?: string }) {
  return <ChevronDown size={16} className={className} aria-hidden />
}

/** The button of a date field that opens the calendar. */
export function CalendarIcon() {
  return <CalendarDays size={16} aria-hidden />
}
