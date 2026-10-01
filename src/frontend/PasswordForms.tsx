import { useMutation } from '@apollo/client/react'
import { Card, Heading, Link } from '@radix-ui/themes'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FieldForm } from './forms.tsx'
import { ChangePasswordDocument, ResetPasswordDocument } from './gql/generated.ts'
import { SuccessMessage } from './messages.tsx'

export function ChangePasswordForm() {
  const { t } = useTranslation()
  const [changePassword] = useMutation(ChangePasswordDocument)
  const [done, setDone] = useState(false)

  return (
    <section>
      <Heading as="h2" size="4" my="3">
        {t('auth.changeTitle')}
      </Heading>
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
    <Card size="3" style={{ maxWidth: 420, margin: '2rem auto' }}>
      {done ? (
        <>
          <SuccessMessage>{t('auth.setDone')}</SuccessMessage>
          <Link href="/">{t('auth.continueSignIn')}</Link>
        </>
      ) : (
        <>
          <Heading as="h2" size="5" mb="3">
            {t('auth.setTitle')}
          </Heading>
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
