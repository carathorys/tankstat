import { Callout } from '@radix-ui/themes'
import { CircleCheck, CircleX } from 'lucide-react'
import type { ReactNode } from 'react'
import { useErrorText } from './i18n/errors.ts'

/** Pass a caught/Apollo `error` (translated by its error key) or plain `children`. */
export function ErrorMessage({ error, children }: { error?: unknown; children?: ReactNode }) {
  const errorText = useErrorText()

  return (
    <Callout.Root color="red" role="alert" size="1" my="2" className="tk-appear">
      <Callout.Icon>
        <CircleX size={16} />
      </Callout.Icon>
      <Callout.Text>{children ?? errorText(error)}</Callout.Text>
    </Callout.Root>
  )
}

export function SuccessMessage({ children }: { children: ReactNode }) {
  return (
    <Callout.Root color="green" role="status" size="1" my="2" className="tk-appear">
      <Callout.Icon>
        <CircleCheck size={16} />
      </Callout.Icon>
      <Callout.Text>{children}</Callout.Text>
    </Callout.Root>
  )
}
