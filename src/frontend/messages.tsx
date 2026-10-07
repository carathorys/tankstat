import Alert from '@mui/material/Alert'
import { CircleCheck, CircleX } from 'lucide-react'
import type { ReactNode } from 'react'
import { useErrorText } from './i18n/errors.ts'

/** Pass a caught/Apollo `error` (translated by its error key) or plain `children`. */
export function ErrorMessage({ error, children }: { error?: unknown; children?: ReactNode }) {
  const errorText = useErrorText()
  return (
    <Alert severity="error" role="alert" icon={<CircleX size={16} aria-hidden />} className="tk-appear" sx={{ my: 1 }}>
      {children ?? errorText(error)}
    </Alert>
  )
}

export function SuccessMessage({ children }: { children: ReactNode }) {
  return (
    <Alert severity="success" role="status" icon={<CircleCheck size={16} aria-hidden />} className="tk-appear" sx={{ my: 1 }}>
      {children}
    </Alert>
  )
}
