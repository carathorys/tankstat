/**
 * Set in this tab when the wizard's last step restarts the server into a provider's sign-in with nobody named administrator: the page is
 * left for the provider's sign-in, and coming back the wizard goes on at its last step, the claim (the code asked once more: it is kept
 * nowhere). Cleared as the setup ends.
 */
const CLAIM_NEXT = 'tankstat.setup.claimNext'

export const remember = (on: boolean) => {
  try {
    if (on) sessionStorage.setItem(CLAIM_NEXT, '1')
    else sessionStorage.removeItem(CLAIM_NEXT)
  } catch {
    // no storage (a private window): the wizard starts at its first step again, with what was saved
  }
}

export const claimNext = () => {
  try {
    return sessionStorage.getItem(CLAIM_NEXT) === '1'
  } catch {
    return false
  }
}
