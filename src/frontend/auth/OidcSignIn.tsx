import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import Link from '@mui/material/Link'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation } from 'react-router'
import { ErrorMessage } from '../messages.tsx'
import { navigation } from '../navigation.ts'
import { failureText, oidcLoginUrl, signedOut } from './oidc.ts'

/**
 * Signing in with an identity provider (Auth:Mode=Oidc). On a fresh visit the browser is sent to the provider at once and comes back to
 * this very page; the screen only shows a spinner and a link for the case that the navigation did not happen (a browser that blocked it).
 * After Sign out it waits for a click instead (the provider's own session would sign the user straight back in), and a sign-in
 * that failed (`?signIn=failed&reason=…`, see OidcFailures on the server) says why and offers to try again. Never rendered while the
 * session query has not answered, so a server that is down does not send anyone away.
 */
export function OidcSignIn() {
  const { t } = useTranslation()
  const { pathname, search } = useLocation()
  const params = new URLSearchParams(search)
  const [wasSignedOut] = useState(signedOut.isSet)
  const failed = params.get('signIn') === 'failed'
  const loginUrl = oidcLoginUrl(pathname, search)
  const redirected = useRef(false)

  useEffect(() => {
    if (failed || wasSignedOut || redirected.current) return
    redirected.current = true // StrictMode runs effects twice; the ref survives it
    navigation.replace(loginUrl)
  }, [failed, wasSignedOut, loginUrl])

  const signIn = () => {
    signedOut.clear()
    navigation.replace(loginUrl)
  }

  if (failed) {
    return (
      <>
        <Typography component="h2" variant="h4" sx={{ mb: 1.5 }}>
          {t('auth.failedTitle')}
        </Typography>
        <ErrorMessage>{t(failureText(params.get('reason')))}</ErrorMessage>
        <Button size="large" sx={{ mt: 1.5, minHeight: 44 }} onClick={signIn}>
          {t('auth.tryAgain')}
        </Button>
      </>
    )
  }
  if (wasSignedOut) {
    return (
      <>
        <Typography component="h2" variant="h4" sx={{ mb: 1.5 }}>
          {t('auth.signedOutTitle')}
        </Typography>
        <Typography sx={{ mb: 1.5 }}>{t('auth.signedOutHint')}</Typography>
        <Button size="large" sx={{ minHeight: 44 }} onClick={signIn}>
          {t('auth.signIn')}
        </Button>
      </>
    )
  }
  return (
    <>
      <Typography component="h2" variant="h4" sx={{ mb: 1.5 }}>
        {t('auth.signIn')}
      </Typography>
      <Stack direction="row" role="status" sx={{ alignItems: 'center', gap: 1.5, mb: 1.5 }}>
        <CircularProgress size={20} color="inherit" aria-hidden />
        <Typography>{t('auth.redirecting')}</Typography>
      </Stack>
      <Link href={loginUrl} sx={{ display: 'inline-flex', alignItems: 'center', minHeight: 44 }}>
        {t('auth.continueSignIn')}
      </Link>
    </>
  )
}
