import { Button, Flex, Heading, Link, Spinner, Text } from '@radix-ui/themes'
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
        <Heading as="h2" size="5" mb="3">
          {t('auth.failedTitle')}
        </Heading>
        <ErrorMessage>{t(failureText(params.get('reason')))}</ErrorMessage>
        <Button mt="3" size="3" style={{ minHeight: 44 }} onClick={signIn}>
          {t('auth.tryAgain')}
        </Button>
      </>
    )
  }
  if (wasSignedOut) {
    return (
      <>
        <Heading as="h2" size="5" mb="3">
          {t('auth.signedOutTitle')}
        </Heading>
        <Text as="p" mb="3">
          {t('auth.signedOutHint')}
        </Text>
        <Button size="3" style={{ minHeight: 44 }} onClick={signIn}>
          {t('auth.signIn')}
        </Button>
      </>
    )
  }
  return (
    <>
      <Heading as="h2" size="5" mb="3">
        {t('auth.signIn')}
      </Heading>
      <Flex align="center" gap="3" mb="3" role="status">
        <Spinner size="3" />
        <Text>{t('auth.redirecting')}</Text>
      </Flex>
      <Link href={loginUrl} style={{ display: 'inline-flex', alignItems: 'center', minHeight: 44 }}>
        {t('auth.continueSignIn')}
      </Link>
    </>
  )
}
