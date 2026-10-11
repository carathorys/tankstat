import Alert from '@mui/material/Alert'
import { AlertTriangle, CircleCheck, CircleX, CloudOff } from 'lucide-react'
import type { ReactNode } from 'react'
import { useErrorText } from './i18n/errors.ts'
import { OfflineError } from './offline/errors.ts'

/** Pass a caught/Apollo `error` (translated by its error key) or plain `children`. */
export function ErrorMessage({ error, children }: { error?: unknown; children?: ReactNode }) {
  const errorText = useErrorText()
  // Not sent because the server is out of reach: a calm note, not an alarm (the top bar says offline; what was loaded stays).
  if (error instanceof OfflineError && children === undefined)
    return (
      <Alert severity="info" role="status" icon={<CloudOff size={16} aria-hidden />} className="tk-appear" sx={{ my: 1 }}>
        {errorText(error)}
      </Alert>
    )
  return (
    <Alert severity="error" role="alert" icon={<CircleX size={16} aria-hidden />} className="tk-appear" sx={{ my: 1 }}>
      {children ?? errorText(error)}
    </Alert>
  )
}

/** Done, but a part of it failed and the user has to act on it (say, hand a link over that could not be e-mailed). */
export function WarningMessage({ children }: { children: ReactNode }) {
  return (
    <Alert severity="warning" role="alert" icon={<AlertTriangle size={16} aria-hidden />} className="tk-appear" sx={{ my: 1 }}>
      {children}
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
