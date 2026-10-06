import { useMutation } from '@apollo/client/react'
import Card from '@mui/material/Card'
import Link from '@mui/material/Link'
import Typography from '@mui/material/Typography'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FieldForm } from './forms/FieldForm.tsx'
import { ChangePasswordDocument, ResetPasswordDocument } from './gql/generated.ts'
import { SuccessMessage } from './messages.tsx'

export function ChangePasswordForm() {
  const { t } = useTranslation()
  const [changePassword] = useMutation(ChangePasswordDocument)
  const [done, setDone] = useState(false)

  return (
    <section>
      <Typography component="h2" variant="h5" sx={{ my: 1.5 }}>
        {t('auth.changeTitle')}
      </Typography>
      {done && <SuccessMessage>{t('auth.changed')}</SuccessMessage>}
      <FieldForm
        fields={[
          { name: 'current', label: 'fields.currentPassword', type: 'password', autoComplete: 'current-password' },
          { name: 'next', label: 'fields.newPassword', type: 'password', autoComplete: 'new-password' },
        ]}
        submitLabel="auth.changeSubmit"
        onSubmit={async (v) => {
          await changePassword({ variables: { input: { currentPassword: v.current ?? '', newPassword: v.next ?? '' } } })
          setDone(true)
        }}
      />
    </section>
  )
}

/** Opened from the link in a reset/setup e-mail (or one an administrator handed over). */
export function ResetPasswordView({ token }: { token: string }) {
  const { t } = useTranslation()
  const [resetPassword] = useMutation(ResetPasswordDocument)
  const [done, setDone] = useState(false)

  return (
    <Card sx={{ maxWidth: 420, mx: 'auto', my: 4, p: 3 }}>
      {done ? (
        <>
          <SuccessMessage>{t('auth.setDone')}</SuccessMessage>
          <Link href="/">{t('auth.continueSignIn')}</Link>
        </>
      ) : (
        <>
          <Typography component="h2" variant="h4" sx={{ mb: 1.5 }}>
            {t('auth.setTitle')}
          </Typography>
          <FieldForm
            fields={[{ name: 'password', label: 'fields.newPassword', type: 'password', autoComplete: 'new-password' }]}
            submitLabel="auth.setSubmit"
            onSubmit={async (v) => {
              await resetPassword({ variables: { input: { token, newPassword: v.password ?? '' } } })
              setDone(true)
            }}
          />
        </>
      )}
    </Card>
  )
}
