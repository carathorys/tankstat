# Authentication and access control

For operators: how users sign in (none, standalone, OpenID Connect or a trusted proxy), every setting of each mode, e-mail for password links, and who may see and change what.

Authentication is configured through the `Auth` and `Smtp` sections. Every setting can be given in `appsettings.json` or as an **environment variable**, where `:` becomes `__` (double underscore): `Auth:Oidc:ClientId` is `Auth__Oidc__ClientId`. Environment variables override `appsettings.json`. List settings use an index: `Auth__AdminEmails__0=a@x.com`, `Auth__AdminEmails__1=b@x.com`. Names are case-insensitive, and enum values (`Auth__Mode`) are matched case-insensitively. A misconfiguration stops the app at start with a message naming the missing setting.

> **The shipped `appsettings.json` sets `Auth:Mode=None`: there is no authentication, everyone sees and changes everything, and there is no administrator.** Always set `Auth__Mode` explicitly (and, for `Standalone`, the administrator credentials) before exposing the app. Never put real credentials in a committed file.

## Choosing the mode

`Auth__Mode` selects how users are authenticated. Default when nothing is configured: `None`.

| `Auth__Mode` value | Who authenticates | Required settings (see the tables below) | Notes |
| --- | --- | --- | --- |
| `None` | nobody | none | **Unsafe.** Everyone sees and changes everything; the UI shows a warning. Data belongs to an anonymous owner; there are no profile pictures, no administration. |
| `Standalone` | the app itself | `Auth__Standalone__AdminEmail` and `Auth__Standalone__AdminPassword` on the first start (while no local administrator exists) | Login with e-mail and password, password change, one-time reset links, lockout, user management by administrators (create, edit name and e-mail, reset link, disable, delete). |
| `Oidc` | an OpenID Connect provider | `Auth__Oidc__Authority`, `Auth__Oidc__ClientId`, `Auth__Oidc__ClientSecret` | Server-side authorization-code flow with PKCE; the browser only gets the two HttpOnly cookies of a session (see *Sessions* below). Redirect URI to register: `https://<your-host>/auth/oidc/callback`. |
| `ProxyHeader` | a trusted reverse proxy (Authelia, Authentik, Cloudflare Access, oauth2-proxy, ...) | `Auth__ProxyHeader__TrustedProxies__0` (at least one) | The app trusts a user header, but only from the listed proxy addresses. There is no login or logout in the app. |

All modes end in the same place: a user record in the database that owns data.

## Sessions (Standalone and OIDC)

A signed-in browser holds two HttpOnly cookies, so no script on the page ever sees a token:

- **`tankstat.session`, the access cookie** (`SameSite=Lax`, Secure on HTTPS): read on every request, valid for `Auth__AccessTokenMinutes` (15 minutes), not sliding. It survives closing the browser (also after an OIDC sign-in), for those few minutes.
- **`tankstat.refresh`, the refresh cookie** (same attributes, but only sent to `/auth/token/...`): the refresh token of the device's session, valid for `Auth__RefreshTokenDays` (90 days) from its last use.

When the access cookie ran out, the app trades the refresh token at `POST /auth/token/refresh` for a new access cookie and a **new** refresh token, and sends the request again; the person notices nothing (also when the app is opened again later: before it shows the sign-in screen it tries the refresh cookie once). A device that is used at least once in 90 days stays signed in, which also lets the installed app come back after weeks. Each device's session is a row of `UserSessions` that stores only a hash of its secret (and the secret encrypted with the key ring, see below).

What ends a session (at once: the access cookie names its session, which is checked on every request like the user is):

- **Sign out** (`POST /auth/token/logout`, or the `logout` mutation): this device's session, even after its access cookie ran out.
- **The Account page's *Signed-in devices*** (`mySessions`, `revokeSession(id)`, `revokeOtherSessions`): you see every device you are signed in on (a coarse name worked out from the browser, such as "Firefox on Linux", never the raw User-Agent; when it signed in and was last used) and sign out one of them, or every one but this.
- **Changing your password**: every other device; the one that changed it stays signed in. **Resetting it with a link**, an **administrator setting it**, or **disabling the user**: every device.
- **A refresh token used again**: a token that was already traded in is accepted once more within `Auth__RefreshRotationGraceSeconds` (and answered with the current one, so tabs that refreshed at the same moment agree); used later, someone else must have a copy, so the session ends (a Warning in the log names it by id).
- Not using the device for `Auth__RefreshTokenDays`. Ended and expired sessions are deleted a week later.

The token endpoints only answer requests with the header `X-Requested-With: fetch`, which a page of another site cannot send, and do not exist in `None` and `ProxyHeader` modes (there the proxy signs every request in itself). The cookies are protected by the key ring (`DataProtection:KeysPath`, see [Configuration](configuration.md)): keep it with the data, or every restart signs everybody out.

## General settings (every mode)

| Environment variable | Type / values | Default | Used in | Meaning |
| --- | --- | --- | --- | --- |
| `Auth__Mode` | `None`, `Standalone`, `Oidc`, `ProxyHeader` | `None` | all | The authentication mode. |
| `Auth__PublicUrl` | URL, e.g. `https://tankstat.example.com` | empty | `Standalone` | Public base URL of the app, used to build the password-setup link (`<PublicUrl>/?resetToken=...`). **Required when `Smtp__Host` and `Smtp__From` are set.** Without it, administrators get the token and hand the link over themselves. |
| `Auth__AccessTokenMinutes` | integer (1-1440) | `15` | `Standalone`, `Oidc` | How long the access cookie lasts; a device that is still signed in gets a new one silently. |
| `Auth__RefreshTokenDays` | integer (1-3650) | `90` | `Standalone`, `Oidc` | How long a device stays signed in **without being used**; every use starts the period again. |
| `Auth__RefreshRotationGraceSeconds` | integer (0-3600) | `60` | `Standalone`, `Oidc` | How long a refresh token the device already traded in is still accepted (an answer that never arrived, two tabs refreshing at once). |
| `Auth__AdminEmails__0`, `Auth__AdminEmails__1`, ... | list of e-mail addresses or user names | empty | `Oidc`, `ProxyHeader` | Identities that are made administrators when they sign in. Matched case-insensitively against the provider's subject **or** the e-mail. Configuration only promotes: demoting an administrator is done in the UI. |

## Standalone settings (`Auth__Mode=Standalone`)

| Environment variable | Type | Default | Required | Meaning |
| --- | --- | --- | --- | --- |
| `Auth__Standalone__AdminEmail` | e-mail | empty | on the first start | E-mail (login name) of the first administrator. Read only while no local administrator exists; afterwards it is ignored. |
| `Auth__Standalone__AdminPassword` | string, 10-128 characters | empty | on the first start | Password of the first administrator (the normal password rules apply). The app refuses to start if no administrator exists and these two are missing. |
| `Auth__Standalone__MaxFailedAttempts` | integer | `5` | no | Failed logins before the account is locked. |
| `Auth__Standalone__LockoutMinutes` | integer (minutes) | `15` | no | How long a locked account stays locked. |
| `Auth__Standalone__ResetTokenMinutes` | integer (minutes), 1-43200 | `60` | no | How long a password setup/reset link is valid (it can be used once). At most 30 days; the app does not start with a larger value. |
| `Auth__Standalone__ResetCooldownMinutes` | integer (minutes), 0-1440 | `5` | no | After a link was issued to an account, a request for another one through *Forgot your password?* sends nothing for this long (the answer looks the same), so nobody can flood a mailbox with reset mails. `0` turns the limit off. Links an administrator issues are never held back. |
| `Auth__Standalone__AllowAdminSetPassword` | `true` / `false` | `false` | no | Lets administrators set a user's password directly in the UI (the user is signed out everywhere). Off by default: administrators then only issue reset links. |

Deleting a user: if they own vehicles or logs, the administrator chooses to move everything (including trashed items, authorship and sharing grants) to another user, or to delete it permanently. The last active administrator and your own account cannot be deleted. Name and e-mail of `Oidc`/`ProxyHeader` users come from the provider and are not editable.

Facts that matter when configuring: passwords need 10 to 128 characters; changing or resetting a password signs out every other session; administrators create users in the UI, which issues a one-time setup link (e-mailed when `Smtp` is configured, otherwise shown to the administrator); users can request a reset link themselves when `Smtp` is configured (always answered the same way, so accounts cannot be discovered; at most one e-mail per `ResetCooldownMinutes` per account, and without `Smtp` nothing is issued); each new link, also one an administrator issues, replaces the account's earlier ones, so only the latest link works; how long a device stays signed in is described under *Sessions* below.

## OpenID Connect settings (`Auth__Mode=Oidc`)

| Environment variable | Type | Default | Required | Meaning |
| --- | --- | --- | --- | --- |
| `Auth__Oidc__Authority` | URL of the issuer, e.g. `https://id.example.com` | empty | yes | The provider's issuer URL (its `/.well-known/openid-configuration` is read from here). A non-HTTPS authority is accepted for local development only. |
| `Auth__Oidc__ClientId` | string | empty | yes | Client id registered at the provider. |
| `Auth__Oidc__ClientSecret` | string | empty | yes | Client secret (confidential client). |
| `Auth__Oidc__Scopes__0`, `Auth__Oidc__Scopes__1`, ... | list of scopes | `openid`, `profile`, `email` | no | Scopes requested. The default three are always requested; values given here are **added** to them (listing a default again is harmless), so only give extra scopes such as `groups`. The defaults can only be changed in code. |

Provider-side setup: register the redirect URI `https://<your-host>/auth/oidc/callback` (and, if the provider asks, the post-logout URI `https://<your-host>/auth/oidc/signed-out`). Claims used: `sub` (required, the stable identity), `email` (ignored when `email_verified` is `false`, so an unverified address cannot claim an administrator e-mail), `name` or `preferred_username` (display name). Users are created at their first sign-in; a disabled user is refused.

How the browser signs in: the app sends a visitor to the provider at once (the sign-in screen shows only a spinner and a *Continue to sign in* link in case the redirect did not happen) and brings them back to the page they opened (`/auth/oidc/login?returnUrl=...`; only same-site paths are accepted). **Sign out** ends the app's session only; the provider's session stays, so the screen after signing out waits for a click on *Sign in* (remembered per browser tab) instead of signing the user straight back in, and a fresh tab signs in silently while the provider session lasts. A sign-in that fails (cancelled at the provider, refused by it, a stale callback, a disabled account) lands on `<page>?signIn=failed&reason=<code>` with an explanation and a *Try again* button; the codes are `access_denied`, `provider_error`, `token_rejected`, `no_subject`, `account_disabled` and `callback_rejected`, and the app's own log line is one Warning, `OIDC sign-in failed: <code> (<exception type>)`, never the provider's text (a disabled account also keeps its refusal line, and the framework may add a line of its own about a token endpoint error). A provider that cannot be reached when the sign-in starts still answers `/auth/oidc/login` with a plain server error.

## Proxy header settings (`Auth__Mode=ProxyHeader`)

| Environment variable | Type | Default | Required | Meaning |
| --- | --- | --- | --- | --- |
| `Auth__ProxyHeader__TrustedProxies__0`, `__1`, ... | list of IP addresses or CIDR ranges, e.g. `10.0.0.0/8`, `172.18.0.5`, `fd00::/8` | empty | **yes, at least one** | The proxies whose headers are trusted. A request from any other address has its user header ignored, otherwise anyone could forge it. |
| `Auth__ProxyHeader__UserHeader` | header name | `X-Forwarded-User` | yes | Header carrying the user name (a single, non-empty value without commas, otherwise the request is rejected). Also matched against `Auth__AdminEmails__n`. |
| `Auth__ProxyHeader__EmailHeader` | header name, or empty to ignore | `X-Forwarded-Email` | no | Header carrying the user's e-mail address. |

The proxy must also strip these headers from incoming client requests, set them itself, and connect from an address listed in `TrustedProxies`. Users are created at their first request; a disabled user is refused.

## E-mail settings (`Smtp__*`, used by Standalone to send password setup/reset links)

Without `Smtp__Host` and `Smtp__From` no e-mail is sent; administrators issue links by hand and hand them over. Setting both requires `Auth__PublicUrl`.

| Environment variable | Type / values | Default | Required | Meaning |
| --- | --- | --- | --- | --- |
| `Smtp__Host` | host name | empty | to enable e-mail | SMTP server. |
| `Smtp__From` | e-mail address | empty | to enable e-mail | Sender address. |
| `Smtp__Port` | integer | `587` | no | SMTP port (`465` for implicit TLS, `25` for plain). |
| `Smtp__Security` | `Auto`, `None`, `StartTls`, `Ssl` | `Auto` | no | Transport security. `Auto` picks from the port. |
| `Smtp__Username` | string | empty | no | Login for the SMTP server (authentication is skipped when empty). |
| `Smtp__Password` | string | empty | no | Password for the SMTP server. |

## Copy-and-adapt examples

**Standalone** (first start; after the administrator exists the two `Admin*` lines are no longer needed):

```sh
Auth__Mode=Standalone
Auth__Standalone__AdminEmail=admin@example.com
Auth__Standalone__AdminPassword='a long password'
Auth__PublicUrl=https://tankstat.example.com
Smtp__Host=smtp.example.com
Smtp__Port=587
Smtp__Username=tankstat@example.com
Smtp__Password=...
Smtp__From=tankstat@example.com
```

**Oidc:**

```sh
Auth__Mode=Oidc
Auth__Oidc__Authority=https://id.example.com
Auth__Oidc__ClientId=tankstat
Auth__Oidc__ClientSecret=...
Auth__AdminEmails__0=you@example.com
```

**ProxyHeader:**

```sh
Auth__Mode=ProxyHeader
Auth__ProxyHeader__TrustedProxies__0=10.0.0.0/8
Auth__ProxyHeader__UserHeader=X-Forwarded-User
Auth__ProxyHeader__EmailHeader=X-Forwarded-Email
Auth__AdminEmails__0=you@example.com
```

**None** (development only): `Auth__Mode=None`.

## Checklist for an automated setup

1. Set `Auth__Mode` explicitly (never rely on the shipped `appsettings.json`).
2. Set every setting marked *required* in the table of that mode; the app tells you at start which one is missing.
3. Behind a TLS-terminating proxy keep `Auth__PublicUrl` and the OIDC redirect URI on the public `https://` address (the session cookie accepts plain HTTP behind the proxy).
4. For `Standalone` with e-mail, set `Auth__PublicUrl` together with `Smtp__Host` and `Smtp__From`.
5. Check the result: `{ session { mode user { email isAdmin } } }` at `/graphql` answers the active mode (and `notices` carries the `AUTH_DISABLED` warning when authentication is off).

## Access control

Every vehicle and refuelling belongs to a user. Access levels are ordered `None < View < Edit < Delete`:

- **Edit** creates, changes, moves to the trash and restores; **Delete** additionally deletes permanently (empties the trash). The same rule applies to every kind of data.
- Owners and administrators implicitly have Delete. An administrator decides what others may do with someone else's data: an instance-wide default (nothing, view, edit or delete) plus per-user grants ("Bob may edit Alice's data").
- **Sharing a vehicle's logs.** Anyone who can edit a vehicle (the owner, an administrator, or an editor) can give another user *Edit* or *Delete* access to **that vehicle's logs** only (generic `ResourceGrant`s, "Logs" feature). The grantee sees the vehicle and works with its logs, but cannot change the vehicle or anything else; nobody can give more access than they hold.

Everything goes through one access layer (`AccessService`), so new kinds of data only need to implement `IOwned`. Without authentication everyone has full access.
