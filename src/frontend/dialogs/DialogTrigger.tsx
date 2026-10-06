import { cloneElement, isValidElement, type MouseEvent, type ReactNode } from 'react'

interface TriggerProps {
  onClick?: (event: MouseEvent<HTMLElement>) => void
}

/** The caller's button that opens a dialog, given the click and what a dialog trigger says to assistive technology. */
export function DialogTrigger({ trigger, open, onOpen }: { trigger?: ReactNode; open: boolean; onOpen: () => void }) {
  if (!isValidElement<TriggerProps>(trigger)) return trigger ?? null
  return cloneElement(trigger, {
    onClick: (event: MouseEvent<HTMLElement>) => {
      trigger.props.onClick?.(event)
      if (!event.defaultPrevented) onOpen()
    },
    'aria-haspopup': 'dialog',
    'aria-expanded': open,
  } as TriggerProps)
}
