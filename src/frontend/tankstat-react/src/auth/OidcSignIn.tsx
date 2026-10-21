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
import { connectivity } from '../offline/connectivity.ts'
import { deviceData } from '../offline/deviceData.ts'
import { OfflineError } from '../offline/errors.ts'
import { useConnectivity } from '../offline/useConnectivity.ts'
import { failureText, oidcLoginUrl, signedOut } from './oidc.ts'
import { tellLeftOverSignOut } from './signedOutHere.ts'

/**
 * Signing in with an identity provider (Auth:Mode=Oidc). On a fresh visit the browser is sent to the provider at once and comes back to
 * this very page; the screen only shows a spinner and a link for the case that the navigation did not happen (a browser that blocked it).
 * After Sign out it waits for a click instead (the provider's own session would sign the user straight back in), and a sign-in
 * that failed (`?signIn=failed&reason=…`, see OidcFailures on the server) says why and offers to try again. Never rendered while the
 * session query has not answered, so a server that is down does not send anyone away. A device signed out while the server was out of
 * reach waits for a click too, in any tab and after a restart (`SignedOutHere`, #162): the provider is the server's, and it may not be there.
 */
export function OidcSignIn() {
  const { t } = useTranslation()
  const { pathname, search } = useLocation()
  const params = new URLSearchParams(search)
  const [wasSignedOut] = useState(signedOut.isSet)
  // Signed out here without the server hearing of it: null until known, so nothing is sent away on a guess.
  const [signedOutHere, setSignedOutHere] = useState<boolean | null>(null)
  // Why the last Sign in did not go to the provider: the server out of reach, or it did not end the session this device signed out of.
  const [stopped, setStopped] = useState<'offline' | 'notTold' | null>(null)
  const { reachable } = useConnectivity()
  const failed = params.get('signIn') === 'failed'
  const loginUrl = oidcLoginUrl(pathname, search)
  const redirected = useRef(false)

  useEffect(() => {
    let live = true
    // A read the device cannot make counts as not signed out here, as for the session (a guess would keep a signed-in person waiting).
    void deviceData.signedOutHere().then(
      (held) => live && setSignedOutHere(held !== null),
      () => live && setSignedOutHere(false),
    )
    return () => {
      live = false
    }
  }, [])

  useEffect(() => {
    if (failed || wasSignedOut || signedOutHere !== false || redirected.current) return
    redirected.current = true // StrictMode runs effects twice; the ref survives it
    navigation.replace(loginUrl)
  }, [failed, wasSignedOut, signedOutHere, loginUrl])

  const signIn = async () => {
    // The provider is reached through the server: without it the page would be gone.
    if (!connectivity.reachable) return setStopped('offline')
    // A sign-out left over is told first, and the browser goes only once the server ended that session: what it navigates to cannot be
    // seen from here, and the sign-out told after its return would end the new session (a read the device cannot make does not hold it up).
    if (!(await tellLeftOverSignOut().catch(() => true))) return setStopped(connectivity.reachable ? 'notTold' : 'offline')
    setStopped(null)
    signedOut.clear()
    navigation.replace(loginUrl)
  }
  // Said as things are now: once the server is back, the note goes.
  const offlineNote =
    stopped === 'notTold' ? <ErrorMessage>{t('auth.signedOutNotTold')}</ErrorMessage> : stopped === 'offline' && !reachable && <ErrorMessage error={new OfflineError()} />

  if (failed) {
    return (
      <>
        <Typography component="h2" variant="h4" sx={{ mb: 1.5 }}>
          {t('auth.failedTitle')}
        </Typography>
        <ErrorMessage>{t(failureText(params.get('reason')))}</ErrorMessage>
        {offlineNote}
        <Button size="large" sx={{ mt: 1.5, minHeight: 44 }} onClick={() => void signIn()}>
          {t('auth.tryAgain')}
        </Button>
      </>
    )
  }
  if (wasSignedOut || signedOutHere) {
    return (
      <>
        <Typography component="h2" variant="h4" sx={{ mb: 1.5 }}>
          {t('auth.signedOutTitle')}
        </Typography>
        <Typography sx={{ mb: 1.5 }}>{t('auth.signedOutHint')}</Typography>
        {offlineNote}
        <Button size="large" sx={{ minHeight: 44 }} onClick={() => void signIn()}>
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
