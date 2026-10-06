# Tankstat

[![CI](https://github.com/carathorys/tankstat/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/carathorys/tankstat/actions/workflows/ci.yml)
[![Release workflow](https://github.com/carathorys/tankstat/actions/workflows/release.yml/badge.svg)](https://github.com/carathorys/tankstat/actions/workflows/release.yml)
[![codecov](https://codecov.io/gh/carathorys/tankstat/graph/badge.svg)](https://codecov.io/gh/carathorys/tankstat)
[![Latest release](https://img.shields.io/github/v/release/carathorys/tankstat?include_prereleases&sort=semver)](https://github.com/carathorys/tankstat/releases)
[![License: MIT](https://img.shields.io/github/license/carathorys/tankstat)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Node LTS](https://img.shields.io/badge/Node-LTS-339933?logo=nodedotjs&logoColor=white)
![TypeScript](https://img.shields.io/badge/TypeScript-3178C6?logo=typescript&logoColor=white)

Self-hosted web app: a React + TypeScript frontend (Vite) and a .NET 10 backend. They talk GraphQL (HotChocolate on the server, Apollo Client in the browser), and the backend stores data with Entity Framework Core. In production, Kestrel serves both the API and the built frontend, which installs as a web app on phones and desktops (see [Install as an app](#install-as-an-app)).

## Prerequisites

[mise](https://mise.jdx.dev) manages the toolchain (Node LTS and .NET 10, see `mise.toml`).

```sh
mise install
mise run install   # npm ci + dotnet restore
```

## Layout

```
src/backend/Tankstat.Api/                 HTTP host: HotChocolate GraphQL at /graphql, static SPA hosting
src/backend/Tankstat.Application/         use cases and ports (e.g. IDatabaseProbe)
src/backend/Tankstat.Domain/              entities (Vehicle, Refueling, User, access grants) and their rules, no dependencies
src/backend/Tankstat.Infrastructure/      EF Core mapping, repositories, database provider selection
src/backend/Tankstat.Migrations.*/        one EF Core migration set per provider (Sqlite, PostgreSql, SqlServer, MySql)
src/frontend/                             React + Vite app: top bar, hamburger menu, pages (vehicles, trash, account, administration); UI built with Radix Themes (@radix-ui/themes), lucide-react icons, motion animations, react-i18next
tests/backend/Tankstat.Domain.UnitTests/          entity rules
tests/backend/Tankstat.Application.UnitTests/     services (incl. access and auth rules) against in-memory fakes
tests/backend/Tankstat.Infrastructure.UnitTests/  config binding, repositories (SQLite file), migration/model consistency for every provider
tests/backend/Tankstat.Api.IntegrationTests/      in-process GraphQL tests for every auth mode (WebApplicationFactory)
tests/backend/Tankstat.Api.ApiTests/              black-box contract tests against a running server
tests/frontend/                           Vitest unit (*.unit.test.*) and integration (*.integration.test.*) tests, a folder per feature area
tests/frontend/support/                   shared test helpers: msw handlers (server.ts), fake backends (mocks.tsx), setup
scripts/test-api.sh                       starts the API, runs the API tests, stops it
tests/backend/contracts/openai-compatible/  answers as OpenAI-compatible model servers write them, for the app's adapter tests
```

Photo reading is optional: the app talks to a vision model behind an OpenAI-compatible API through its `IRecognitionProvider` port (see [Reading with an OpenAI-compatible model](#reading-with-an-openai-compatible-model)).

Backend dependencies point inward: Api -> Application, Infrastructure; Infrastructure -> Application; Application -> Domain. Application defines interfaces (ports) that Infrastructure implements, so Application and Domain never reference EF Core or HotChocolate.

The frontend tooling (`package.json`, `vite.config.ts`, `tsconfig*`) lives at the repo root so that both `src/frontend` and `tests/frontend` resolve the same `node_modules`.

## Configuration

The database is chosen through the `Database` section. If nothing is configured, the app falls back to a SQLite file, `tankstat.db`, in the working directory. Values can be set in `appsettings.json` or overridden by environment variables, which take precedence (standard ASP.NET Core order: appsettings.json < appsettings.{Environment}.json < environment variables < command line).

| Setting | Environment variable | Values |
| --- | --- | --- |
| `Database:Provider` | `Database__Provider` | `Sqlite`, `PostgreSql`, `SqlServer`, `MySql` (Oracle provider; MariaDB is not a tested target) |
| `Database:ConnectionString` | `Database__ConnectionString` | provider-specific connection string |

```sh
Database__Provider=PostgreSql Database__ConnectionString="Host=db;Database=tankstat;Username=app;Password=..." \
  dotnet out/Tankstat.Api.dll
```

The connection string may be omitted only for `Sqlite`. The app refuses to start if it is missing for another provider, or if the provider is unknown.

Uploaded pictures (profile pictures, vehicle pictures, photos of refuelings and expenses) are stored as files; only metadata is in the database:

| Setting | Environment variable | Meaning |
| --- | --- | --- |
| `Storage:Path` | `Storage__Path` | folder for uploaded pictures (default `uploads`, relative to the working directory; the Docker image uses `/data/uploads`). Files are organised per owner: `users/<id>/` for avatars and `vehicles/<id>/` for everything of a vehicle (its picture and, below it, the photos of its logs and the photo drafts of entries not saved yet), so a vehicle's files are removed with it; files from older versions stay directly in the folder and keep working |
| `Defaults:DistanceUnit`, `Defaults:VolumeUnit`, `Defaults:Currency` | `Defaults__...` | what a new vehicle / new log starts with (`Kilometers` / `Liters` / `EUR` unless changed) |
| `Defaults:RecurringWarnDays`, `Defaults:RecurringWarnDistance` | `Defaults__...` | how early a new recurring expense starts warning (`30` days / `500` in the vehicle's distance unit unless changed; every schedule can override it) |
| `Notifications:MaxPerHour` | `Notifications__MaxPerHour` | how many notifications about events (such as access changes) a user gets in an hour before further ones are only counted (default `20`, at least `1`; recurring-expense reminders do not count) |
| `Notifications:ReadRetentionDays` | `Notifications__ReadRetentionDays` | how many days a notification is kept after it was read, then it is removed (default `30`, at least `1`; unread ones are kept) |

### Logging

The app writes its log to the console (`docker logs`, the terminal) with the standard .NET logger; there is nothing to install. The shipped `Logging` settings in `appsettings.json` keep the app's own lines (category `Tankstat`) at `Information` and silence what used to drown them: Entity Framework's SQL commands and the HTTP client's per-request lines.

| Level | What is logged |
| --- | --- |
| `Error` | an unexpected error, with its exception and stack trace (the client only sees "Unexpected Execution Error") |
| `Warning` | a refused request (no access), a failed sign-in, a failed password change, a lockout, a refused sign-in through an identity provider, a malformed or untrusted proxy header, a database or model server that cannot be used, an upload folder that could not be removed, a photo that could not be attached |
| `Information` | start-up (database migrations applied or up to date, the first administrator), sign-ins and sign-outs, password changes and reset requests, what an administrator does to users and to access, sharing a vehicle's logs, imports, emptied trashes, which model and server photo reading uses and where its system prompt comes from, a database or model server that is back |
| `Debug` | ordinary changes (a vehicle, refueling, expense, schedule, chart, picture or photo added, changed, trashed or restored), every GraphQL request with its time and operation, errors a client causes (a validation failure, something missing, a request the server turns down), sessions that are no longer valid and why, and the trail of each photo a model reads (see [Reading with an OpenAI-compatible model](#reading-with-an-openai-compatible-model)) |

**Only ids, counts and reasons are logged** by the app's own lines: users, vehicles, logs and pictures by id, never e-mail addresses, names, license plates, notes, amounts, odometer readings, passwords, reset links, API keys, the subject an identity provider sends, or values read from photos (why a value was dropped is logged as a code such as `OdometerBelowLatest`, never the value). The one exception is opt-in: `Recognition__OpenAiCompatible__LogTraffic=true` also writes the prompts and what a model read, never the photo or the key, to debug a model ([details](#seeing-what-is-sent-to-the-model)). A failed sign-in names the account by its id (or says "unknown account"), not by the address that was typed. Nothing a client sends is logged as it came: not GraphQL variables or documents, not the text of a request error, fields are named the way the schema names them, and the endpoints of the picture and import uploads appear as their route pattern, not as the path that was asked for.

That promise has an edge. The text of an unexpected error is written as the framework or the database wrote it, and a database server can quote the values of a failed statement in it (for a duplicate key, for example). Lines of the framework and its libraries (`Microsoft.*`, `System.*`) are written as they come, such as an identity provider's error description. And **do not lower `Microsoft.AspNetCore` below `Warning`**: its request lines hold the whole address of a request, which includes the token of a password reset link.

Change what is shown with the usual settings (`Logging:LogLevel:...` in `appsettings.json`, or environment variables):

```sh
Logging__LogLevel__Tankstat=Debug      # every Debug line of the app (the default when running with mise run dev:api)
Logging__LogLevel__Tankstat=Warning    # quieter: only warnings and errors of the app ("Default" is for the framework's own categories)

# How it looks: name the formatter first, its options only apply to the formatter that is named.
Logging__Console__FormatterName=json   # one JSON object per line, for a log collector
Logging__Console__FormatterName=simple # or the readable one, which these options adjust:
Logging__Console__FormatterOptions__SingleLine=true                         #   one line per entry
Logging__Console__FormatterOptions__TimestampFormat="yyyy-MM-dd HH:mm:ss "  #   with the time (there is none by default)
Logging__Console__FormatterOptions__UseUtcTimestamp=true
Logging__Console__FormatterOptions__IncludeScopes=true                      #   the scopes of a request, see below
```

With scopes on, each line of a signed-in user's request carries `UserId: <id>`, next to the framework's own scopes (trace and connection ids and the **request path**, which is what the client asked for). Prefer the `json` formatter with scopes: it escapes the path. A narrower category works like the first line, for example `Logging__LogLevel__Tankstat.Application.Users=Debug` for sign-ins and user administration only (a name with dots can be set through Docker or `env`, not through the shell's `export`). The browser has a log of its own: a failed request or a crashed page is written to the browser's console, and nothing is sent to the server.

## Authentication and access control

Authentication is configured through the `Auth` and `Smtp` sections. Every setting can be given in `appsettings.json` or as an **environment variable**, where `:` becomes `__` (double underscore): `Auth:Oidc:ClientId` is `Auth__Oidc__ClientId`. Environment variables override `appsettings.json`. List settings use an index: `Auth__AdminEmails__0=a@x.com`, `Auth__AdminEmails__1=b@x.com`. Names are case-insensitive, and enum values (`Auth__Mode`) are matched case-insensitively. A misconfiguration stops the app at start with a message naming the missing setting.

> **The shipped `appsettings.json` sets `Auth:Mode=None`: there is no authentication, everyone sees and changes everything, and there is no administrator.** Always set `Auth__Mode` explicitly (and, for `Standalone`, the administrator credentials) before exposing the app. Never put real credentials in a committed file.

### Choosing the mode

`Auth__Mode` selects how users are authenticated. Default when nothing is configured: `None`.

| `Auth__Mode` value | Who authenticates | Required settings (see the tables below) | Notes |
| --- | --- | --- | --- |
| `None` | nobody | none | **Unsafe.** Everyone sees and changes everything; the UI shows a warning. Data belongs to an anonymous owner; there are no profile pictures, no administration. |
| `Standalone` | the app itself | `Auth__Standalone__AdminEmail` and `Auth__Standalone__AdminPassword` on the first start (while no local administrator exists) | Login with e-mail and password, password change, one-time reset links, lockout, user management by administrators (create, edit name and e-mail, reset link, disable, delete). |
| `Oidc` | an OpenID Connect provider | `Auth__Oidc__Authority`, `Auth__Oidc__ClientId`, `Auth__Oidc__ClientSecret` | Server-side authorization-code flow with PKCE; the browser only gets an HttpOnly session cookie. Redirect URI to register: `https://<your-host>/auth/oidc/callback`. |
| `ProxyHeader` | a trusted reverse proxy (Authelia, Authentik, Cloudflare Access, oauth2-proxy, ...) | `Auth__ProxyHeader__TrustedProxies__0` (at least one) | The app trusts a user header, but only from the listed proxy addresses. There is no login or logout in the app. |

All modes end in the same place: a user record in the database that owns data.

### General settings (every mode)

| Environment variable | Type / values | Default | Used in | Meaning |
| --- | --- | --- | --- | --- |
| `Auth__Mode` | `None`, `Standalone`, `Oidc`, `ProxyHeader` | `None` | all | The authentication mode. |
| `Auth__PublicUrl` | URL, e.g. `https://tankstat.example.com` | empty | `Standalone` | Public base URL of the app, used to build the password-setup link (`<PublicUrl>/?resetToken=...`). **Required when `Smtp__Host` and `Smtp__From` are set.** Without it, administrators get the token and hand the link over themselves. |
| `Auth__AdminEmails__0`, `Auth__AdminEmails__1`, ... | list of e-mail addresses or user names | empty | `Oidc`, `ProxyHeader` | Identities that are made administrators when they sign in. Matched case-insensitively against the provider's subject **or** the e-mail. Configuration only promotes: demoting an administrator is done in the UI. |

### Standalone settings (`Auth__Mode=Standalone`)

| Environment variable | Type | Default | Required | Meaning |
| --- | --- | --- | --- | --- |
| `Auth__Standalone__AdminEmail` | e-mail | empty | on the first start | E-mail (login name) of the first administrator. Read only while no local administrator exists; afterwards it is ignored. |
| `Auth__Standalone__AdminPassword` | string, 10-128 characters | empty | on the first start | Password of the first administrator (the normal password rules apply). The app refuses to start if no administrator exists and these two are missing. |
| `Auth__Standalone__MaxFailedAttempts` | integer | `5` | no | Failed logins before the account is locked. |
| `Auth__Standalone__LockoutMinutes` | integer (minutes) | `15` | no | How long a locked account stays locked. |
| `Auth__Standalone__ResetTokenMinutes` | integer (minutes) | `60` | no | How long a password setup/reset link is valid (it can be used once). |
| `Auth__Standalone__AllowAdminSetPassword` | `true` / `false` | `false` | no | Lets administrators set a user's password directly in the UI (the user is signed out everywhere). Off by default: administrators then only issue reset links. |

Deleting a user: if they own vehicles or logs, the administrator chooses to move everything (including trashed items, authorship and sharing grants) to another user, or to delete it permanently. The last active administrator and your own account cannot be deleted. Name and e-mail of `Oidc`/`ProxyHeader` users come from the provider and are not editable.

Facts that matter when configuring: passwords need 10 to 128 characters; changing or resetting a password signs out every other session; administrators create users in the UI, which issues a one-time setup link (e-mailed when `Smtp` is configured, otherwise shown to the administrator); users can request a reset link themselves (always answered the same way, so accounts cannot be discovered); the session cookie is `tankstat.session` (HttpOnly, `SameSite=Lax`, valid 14 days, sliding).

### OpenID Connect settings (`Auth__Mode=Oidc`)

| Environment variable | Type | Default | Required | Meaning |
| --- | --- | --- | --- | --- |
| `Auth__Oidc__Authority` | URL of the issuer, e.g. `https://id.example.com` | empty | yes | The provider's issuer URL (its `/.well-known/openid-configuration` is read from here). A non-HTTPS authority is accepted for local development only. |
| `Auth__Oidc__ClientId` | string | empty | yes | Client id registered at the provider. |
| `Auth__Oidc__ClientSecret` | string | empty | yes | Client secret (confidential client). |
| `Auth__Oidc__Scopes__0`, `Auth__Oidc__Scopes__1`, ... | list of scopes | `openid`, `profile`, `email` | no | Scopes requested. The default three are always requested; values given here are **added** to them (listing a default again is harmless), so only give extra scopes such as `groups`. The defaults can only be changed in code. |

Provider-side setup: register the redirect URI `https://<your-host>/auth/oidc/callback` (and, if the provider asks, the post-logout URI `https://<your-host>/auth/oidc/signed-out`). Claims used: `sub` (required, the stable identity), `email` (ignored when `email_verified` is `false`, so an unverified address cannot claim an administrator e-mail), `name` or `preferred_username` (display name). Users are created at their first sign-in; a disabled user is refused.

How the browser signs in: the app sends a visitor to the provider at once (the sign-in screen shows only a spinner and a *Continue to sign in* link in case the redirect did not happen) and brings them back to the page they opened (`/auth/oidc/login?returnUrl=...`; only same-site paths are accepted). **Sign out** ends the app's session only; the provider's session stays, so the screen after signing out waits for a click on *Sign in* (remembered per browser tab) instead of signing the user straight back in, and a fresh tab signs in silently while the provider session lasts. A sign-in that fails (cancelled at the provider, refused by it, a stale callback, a disabled account) lands on `<page>?signIn=failed&reason=<code>` with an explanation and a *Try again* button; the codes are `access_denied`, `provider_error`, `token_rejected`, `no_subject`, `account_disabled` and `callback_rejected`, and the app's own log line is one Warning, `OIDC sign-in failed: <code> (<exception type>)`, never the provider's text (a disabled account also keeps its refusal line, and the framework may add a line of its own about a token endpoint error). A provider that cannot be reached when the sign-in starts still answers `/auth/oidc/login` with a plain server error.

### Proxy header settings (`Auth__Mode=ProxyHeader`)

| Environment variable | Type | Default | Required | Meaning |
| --- | --- | --- | --- | --- |
| `Auth__ProxyHeader__TrustedProxies__0`, `__1`, ... | list of IP addresses or CIDR ranges, e.g. `10.0.0.0/8`, `172.18.0.5`, `fd00::/8` | empty | **yes, at least one** | The proxies whose headers are trusted. A request from any other address has its user header ignored, otherwise anyone could forge it. |
| `Auth__ProxyHeader__UserHeader` | header name | `X-Forwarded-User` | yes | Header carrying the user name (a single, non-empty value without commas, otherwise the request is rejected). Also matched against `Auth__AdminEmails__n`. |
| `Auth__ProxyHeader__EmailHeader` | header name, or empty to ignore | `X-Forwarded-Email` | no | Header carrying the user's e-mail address. |

The proxy must also strip these headers from incoming client requests, set them itself, and connect from an address listed in `TrustedProxies`. Users are created at their first request; a disabled user is refused.

### E-mail settings (`Smtp__*`, used by Standalone to send password setup/reset links)

Without `Smtp__Host` and `Smtp__From` no e-mail is sent; administrators issue links by hand and hand them over. Setting both requires `Auth__PublicUrl`.

| Environment variable | Type / values | Default | Required | Meaning |
| --- | --- | --- | --- | --- |
| `Smtp__Host` | host name | empty | to enable e-mail | SMTP server. |
| `Smtp__From` | e-mail address | empty | to enable e-mail | Sender address. |
| `Smtp__Port` | integer | `587` | no | SMTP port (`465` for implicit TLS, `25` for plain). |
| `Smtp__Security` | `Auto`, `None`, `StartTls`, `Ssl` | `Auto` | no | Transport security. `Auto` picks from the port. |
| `Smtp__Username` | string | empty | no | Login for the SMTP server (authentication is skipped when empty). |
| `Smtp__Password` | string | empty | no | Password for the SMTP server. |

### Copy-and-adapt examples

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

### Checklist for an automated setup

1. Set `Auth__Mode` explicitly (never rely on the shipped `appsettings.json`).
2. Set every setting marked *required* in the table of that mode; the app tells you at start which one is missing.
3. Behind a TLS-terminating proxy keep `Auth__PublicUrl` and the OIDC redirect URI on the public `https://` address (the session cookie accepts plain HTTP behind the proxy).
4. For `Standalone` with e-mail, set `Auth__PublicUrl` together with `Smtp__Host` and `Smtp__From`.
5. Check the result: `{ session { mode user { email isAdmin } } }` at `/graphql` answers the active mode (and `notices` carries the `AUTH_DISABLED` warning when authentication is off).

**Who may see and change what.** Every vehicle and refuelling belongs to a user. Access levels are ordered `None < View < Edit < Delete`:

- **Edit** creates, changes, moves to the trash and restores; **Delete** additionally deletes permanently (empties the trash). The same rule applies to every kind of data.
- Owners and administrators implicitly have Delete. An administrator decides what others may do with someone else's data: an instance-wide default (nothing, view, edit or delete) plus per-user grants ("Bob may edit Alice's data").
- **Sharing a vehicle's logs.** Anyone who can edit a vehicle (the owner, an administrator, or an editor) can give another user *Edit* or *Delete* access to **that vehicle's logs** only (generic `ResourceGrant`s, "Logs" feature). The grantee sees the vehicle and works with its logs, but cannot change the vehicle or anything else; nobody can give more access than they hold.

Everything goes through one access layer (`AccessService`), so new kinds of data only need to implement `IOwned`. Without authentication everyone has full access.

## Using the app

The app is mobile-first and responsive. The start page (*Home*) shows a card per vehicle you own or that is shared with you (a search box finds vehicles by name or license plate, the server does the searching, and more cards load as you scroll to the end of the list; a *Show more* button does the same without scrolling), and is where you add vehicles. *Arrange* (shown from two vehicles on; it lists up to 200 vehicles) puts the cards in your own order, saved with your account: the vehicles you arranged come first, the rest follow by name. Each card shows: its picture as the background (a gradient without one), plate and fuel type, a badge when the vehicle is shared with you, the latest odometer, average consumption, last fill-up, this month's spending and a small six-month trend, and what is overdue or due soon among the vehicle's recurring expenses. If you may add logs to the vehicle, the card also has quick actions: **Refuel** and **Expense** open the same dialogs as the vehicle page, and the tick next to a schedule that needs attention opens **Mark as done**, all without leaving *Home*; afterwards only that card is refreshed, in place. People who may only view the vehicle's logs see no buttons. A navigation menu opened by the hamburger button in the top bar gives access to:

- **Vehicles** (administrators only; never available with `Auth:Mode=None`, where there is no administrator): the grid of all vehicles of everyone, sorted and paged by the server, with edit and delete. Everyone else sees and manages their own vehicles on *Home* (editing and deleting appear for vehicles you may edit).
- **Vehicle page** (tabs; a banner with the vehicle's picture on top): **Dashboard** (key figures, ready-made charts and your own charts), **Refuelings** (the logs: add, edit, move to trash; sortable, paged, selectable columns; in the add and edit dialogs any two of volume, price per unit and total give the third, and once all three are filled the one you typed longest ago is the one that follows, the price per unit only being a help for typing that is not stored), **Expenses** (service, insurance, parking, ...: title, free-text category, a cost with its own currency, optional odometer; same access rules as the logs), **Details** (owner, fuel, units, the vehicle's picture) and **Sharing** (give people access to this vehicle's logs; only for those who can edit the vehicle).
- **Import**: bring fuel logs, other costs and recurring expenses from another app (Fuelio CSV today), see below.
- **Notifications** (also the bell in the top bar, which shows how many are unread and the latest ones): what you were told, newest first; mark one or all read. See *Notifications* below.
- **Trash**: deleting moves a vehicle, a refuelling or an expense to the trash. Tabs *Vehicles*, *Refuelings* and *Expenses*: **restore**, or **empty the trash**, which permanently deletes only what you have Delete access to (the rest stays and the dialog says so).
- **Account** (profile picture; change password in Standalone mode), **Administration** (administrators only) and **Install app** (when your browser can install the web app, see *Install as an app*), plus **Sign out**.

**Recurring expenses.** The *Recurring* tab of a vehicle holds schedules for things that come back: insurance every 12 months, an oil change every 15,000 km or every 12 months, whichever comes first. A schedule repeats by **time**, by **distance (odometer)** or **combined** (whichever is reached first), counts from the day (and odometer) it was last done, and shows when it is next due and whether it is *Upcoming*, *Due soon* (within the warning time and distance of the item; a new schedule starts from `Defaults:RecurringWarnDays` / `Defaults:RecurringWarnDistance`, 30 days and 500 distance units unless configured) or *Overdue*. The current odometer is the vehicle's latest reading from any log; without one a distance-based schedule cannot be judged yet. **Mark as done** covers one service visit: it lists the vehicle's schedules, the one you started from and everything else that is due soon or overdue already ticked (an oil change with its oil and air filters, while the fuel filter keeps its own interval), and starts the next interval of each ticked one from the day and odometer you enter. The amount is optional and covers all of them together, never split: with an amount one normal expense is logged for the visit (so every expense and odometer rule applies), its title and category prefilled from the schedules and editable, and it remembers which schedules it covered (the Expenses grid's *Recurring* column, hidden until you show it); without an amount only the schedules move on. It is all or nothing: if one ticked schedule cannot be done on that day or at that odometer, the message names it and nothing is saved. Photos taken there (the dashboard, the invoice) become the expense's photos; they are kept only when an expense is logged, that is with an amount, or while a photo is still being read and may give the amount. The *Recurring* tab can also tick several schedules and mark them done together (*Mark selected as done*). Cost reports count such an expense once, under its category. Schedules follow the access rules of the vehicle's logs (View sees them, Edit changes them) and are deleted for good, not trashed; deleting a user hands them on or removes them like the vehicle's other data. What is overdue or due soon is listed on the vehicle's card on *Home*.

**Notifications.** You are notified when someone gives you access to a vehicle's logs, changes or takes it away; when someone other than you shares *your* vehicle; when an administrator gives you (or someone, on your data) access to everything a user owns; when an administrator changes the default access for everyone; and when a recurring expense of a vehicle whose logs you may see becomes due soon or overdue (an administrator only for vehicles that concern them personally, not for every vehicle on the instance). Recurring reminders are worked out when you look (the bell asks every minute while the page is open), once per cycle of the schedule: one that turns from due soon to overdue is the same notification again, unread, and marking it done starts a new cycle. To avoid floods, changes to the same thing are folded into the unread notification about it ("3 changes"), a change undone before you read it disappears, and past `Notifications:MaxPerHour` notifications in an hour further ones are only counted in one "more changes" notification. You cannot delete notifications: you mark them read (when is kept), and the system removes them `Notifications:ReadRetentionDays` after that (checked when you look at them; a reminder of a schedule that is still due stays). Nobody else, administrators included, can see yours. With `Auth:Mode=None` they work for the single anonymous user.

**Navigation menu.** On a desktop it is a docked sidebar, open by default; the hamburger button hides and shows it, and the choice is remembered for you (see *Settings follow you*). On a phone it is an overlay drawer, closed until the button is pressed, and closes after choosing a page; it is never remembered.

**Settings follow you.** What the UI remembers is stored with your account, so another device shows the same once you have signed in there: whether the sidebar is open, each grid's columns, their order, the page size and the sort, the language, and the order of your vehicles on *Home*. The browser keeps a copy of every setting (`localStorage`: `tankstat.nav.open`, `tankstat.grid.<grid>`, `tankstat.language`), so the page paints at once; the server is asked once per session and its answer wins, except for a setting you changed meanwhile, and a setting the server does not know keeps the browser's copy. A save that fails (the server is unreachable) only shows in the browser console; the setting still applies in that browser. With `Auth:Mode=None` every visitor shares the anonymous user's settings and arrangement. Deleting a user removes their settings; they are never handed to someone else. The tables are `UiSettings`, `GridSettings` and `VehicleOrders`; the GraphQL fields are `uiSettings`, `updateUiSettings`, `saveGridSettings`, `resetGridSettings` and `setVehicleOrder`. Two devices saving the same setting at the same moment: the later save wins as a whole (the sidebar and the language are one record, each grid another).

**Importing.** The *Import* page walks through four steps: choose the file, choose the target (an existing vehicle you may add logs to, or a new one), review, done. Nothing is saved before you confirm.

- The file is uploaded raw with `POST /imports/{format}` (the answer is a token for the parsed file, kept in memory for 30 minutes); the preview (`importPreview`) and the confirmation (`confirmImport`) are GraphQL.
- **Fuelio** (`fuelio`): the "sync" CSV export of one vehicle (unzip it first). Fuel logs, other costs (with their category names) and the vehicle's name, plate, units and fuel type are read; reminder templates that repeat (every so many months and/or kilometres) become recurring expenses, counting from where the reminder stands now (their amount is not kept; one-off reminders are skipped), a log Fuelio marks as following a missed fill-up keeps that mark, stations, GPS and weather are ignored, income rows are skipped, and a cost with odometer 0 gets no odometer. Fuelio files carry no currency, so you enter one (it defaults to `Defaults:Currency`). Dates keep only the day.
- Rows are saved through the same services as rows typed by hand, so every rule applies (access, odometer order against the vehicle's other readings, future dates). A row that breaks a rule is reported and the rest is still imported. For an existing vehicle you choose whether rows that already exist (same date and odometer; same date, title and amount for expenses) are skipped or imported again.
- More formats (other CSV or JSON sources) are one more `IImportParser` that turns a file into an `ImportBatch`.

**Dashboard and charts.** The first tab of a vehicle shows key figures (spent this month, average consumption of the last ten full fill-ups, odometer, last fill-up), five ready-made charts (monthly costs split into fuel and expenses, consumption, fuel price, expenses by category, distance per month) and the charts people composed. *Add chart* opens a builder: what to measure (total spend, fuel cost, expense cost, fuel volume, distance, average consumption, average fuel price, number of fill-ups), how to group it (month, quarter, year, or expense category for expense costs), the chart type (bars, line, area, donut) and the period: 1 month, 3 months, half a year, one year, this year, last year, all time, or a **custom from-to range**. Only valid combinations are offered, and a live preview shows your own data. A chart is private to its creator unless someone with edit access to the vehicle's logs shares it with everyone who sees the vehicle; only the creator changes it (anyone with delete access may remove a shared one). The numbers are calculated by the server from the stored logs (costs per currency, never mixed), charts are drawn with Recharts, and every chart can be shown as a table (which is what screen readers read).

**Fuel consumption.** Each full fill-up shows the consumption since the previous full one: all fuel added since then (partial top-ups in between count, the previous full fill-up does not) divided by the distance driven, per 100 distance units, in the vehicle's own units (for example L/100 km; miles with gallons read as mpg). Partial fill-ups, the first full fill-up and logs without any distance since the last full one show a dash. When you forgot to log a fill-up, switch on *Missed fill-up before this one* on the next log you enter (the grid marks it *Missed one before*): the fuel of the forgotten fill-up is unknown, so that interval shows a dash too and the calculation starts again from there, instead of showing a consumption that is too low. It is stored on the log and refreshed for the whole vehicle whenever one of its logs is added, changed, trashed, restored or imported (once per import), never on read; the first start after the upgrade calculates it for existing logs. The column can be sorted on the server like the others.

**Units and money.** Every vehicle has its own distance unit (kilometres or miles) and fuel volume unit (litres, US gallons, imperial gallons); numbers are stored exactly as entered and never converted, so the units are locked once a vehicle has logs. Every cost carries its own currency (a reusable `Cost` value: amount plus currency), and a log links to an odometer reading (a reusable `OdometerReading`, without an upper limit; the server checks a new reading against the neighbouring readings of the same vehicle, from any source).

**Pictures.** Users can upload a profile picture (Account page; shown with the Radix `Avatar` wherever users appear) and a picture per vehicle (Details tab). The browser scales the picture down (and crops profile pictures square) and re-encodes it before it is sent; the server still checks the real file type (JPEG, PNG, WebP only; 2 MiB maximum). Pictures are served from `/media/{id}` (immutable, cached; only to signed-in users who may see the owner or vehicle) and uploaded with `PUT /media/me/avatar` and `PUT /media/vehicles/{id}/picture` (removed with `DELETE` on the same paths). These, the photo endpoints below and the import upload (`POST /imports/{format}`) are the only REST endpoints; everything else is GraphQL.

**Photos of refuelings and expenses.** Every refueling and expense can have up to 10 photos (receipts, the pump display, ...). In the add and edit dialogs, **Take photo** opens the phone's camera straight away and **Add photos** picks from the library; photos chosen while adding are uploaded straight away as drafts and attached when the entry is saved (so a failed save loses none; a photo that could not be uploaded is marked and can be tried again), and while editing they are uploaded or removed at once. Photos are scaled to 1600 px in the browser (location data in them is dropped), stored in the log's folder below the vehicle's (see `Storage:Path`) and follow the access rules of the vehicle's logs: whoever may see the log may see its photos, whoever may edit it may add and remove them. They are uploaded with `PUT /media/expenses/{id}/photos` or `PUT /media/refuelings/{id}/photos`, removed with `DELETE` on `.../photos/{imageId}` and shown through `/media/{id}`. Drafts are uploaded with `PUT /media/vehicles/{id}/photo-drafts` (needs edit access to the vehicle's logs), removed with `DELETE /media/photo-drafts/{id}`, attached through the `photoIds` of `logRefueling` / `addExpense`, seen only by their uploader, kept in `vehicles/<id>/drafts/` and removed after a day if no entry was saved with them. A photo disappears from view with its log in the trash and is deleted for good when the log or its vehicle is.

**Reading values from photos (optional).** With a [vision model](#reading-with-an-openai-compatible-model) set up, the server reads the photos picked in the add dialogs of refuelings and expenses and in *Mark as done*, and photos added to a saved refueling or expense in its edit dialog (there the saved values stay: a photo only offers what differs, and fills what is empty, so you can clear a value and save while the photo is read): a dashboard gives the odometer, a fuel receipt the date, litres, price per litre, total and currency (whichever of the three amounts it does not show is worked out from the other two), another receipt the date, shop (as the title), amount and currency. While a photo is being read its thumbnail says *Reading…*; then the values go into the fields you have not changed (the starting values, such as today's date or the last currency, count as unchanged), marked *Read from the photo; check it*. Where you already typed something else, or the app worked the value out from what you typed, the photo's value is only offered (*The photo shows 38.52 · Use it*), never written over yours. Only values the model is sure enough of are filled in (`Recognition:MinConfidence`). The page only talks to Tankstat's own server, which talks to the model server; without one the dialogs work exactly as before.

**Accessibility.** The UI is built to be keyboard- and screen-reader friendly: landmarks and a skip link, a labelled navigation, labelled form controls with linked hints and errors, announced upload/loading status, table semantics with `aria-sort`, 44 px touch targets, dialogs with focus management, and reduced-motion support. Automated axe checks run in the frontend integration tests; colour contrast should still be reviewed by eye. The look is dim and layered: translucent blurred panels (top bar, sidebar) with soft shadows. Changes of state move briefly so you can follow them (a message or a value read from a photo comes in, a row leaves a list, rearranged cards slide to their places, waits show a spinner); with reduced motion only fades remain.

### Install as an app

Tankstat is a progressive web app: served over **HTTPS** (or from `localhost`), it can be installed and opened from the home screen or the dock like any app, full screen, with its own icon.

- **Android (Chrome, Edge, ...)**: the navigation menu shows **Install app** as soon as the browser allows it (Chromium decides when to make the offer; the browser's own menu has the same entry).
- **iPhone and iPad (Safari)**: Safari makes no offer from the page, so **Install app** in the menu shows the steps instead: open the page in Safari, then Share → *Add to Home Screen* → *Add* (other browsers on iOS cannot add to the Home Screen). The installed app has its own cookies: sign in once more there.
- **Desktop (Chrome, Edge)**: the icon in the address bar or **Install app** in the menu. Firefox has no equivalent, and Safari on macOS adds the page to the Dock from its own menu (File → Add to Dock) without telling the page, so the menu entry is missing in both; that is normal.

What it does: a service worker keeps the built pages, scripts, styles and icons, so the app starts at once and still opens without the network (you then see the shell with the "API unreachable" footer: data, pictures and sign-in need the server). Nothing from `/graphql`, `/auth`, `/media` or `/imports` is ever cached, and those paths are never answered with `index.html` by the worker. Updates: the worker looks for a new version at every start; when there is one it takes over and the open tab reloads once, which can be a few seconds after you opened the app (a dialog you had open is gone then; nothing saved is lost). The server sends `/assets/*` (content-hashed names) as immutable and everything with a fixed name (`index.html`, `sw.js`, `manifest.webmanifest`, icons) as `no-cache`, so a release reaches every browser on its next start.

### Grids
Sorting (click a column header), paging and column selection are done by the **server**: the GraphQL query gets `orderBy`, `direction`, `skip`, `take`, and one Boolean variable per optional column that drives `@include`, so hidden columns are not even fetched. The toolbar has a refresh button and a column picker (show/hide and move up/down, which works with touch). Column choices, order, page size and sorting are remembered per grid, for you (a copy in the browser and the server's with your account, see *Settings follow you*). Grid state is coordinated by TanStack Table (manual sorting and pagination: the data itself arrives sorted and paged from the server). Data is re-fetched whenever a page is opened. The layout is mobile-first: on a phone only the essential columns start visible, and the table scrolls horizontally.

### Languages
English and Hungarian (react-i18next); the language menu in the top bar is available before sign-in, defaults to the browser language and is remembered in the browser and, once signed in, with your account (so your other devices switch too; a code the app does not know is ignored). Every visible string is in `src/frontend/i18n/locales/<lang>.json` (typed keys; a test checks that all languages have the same messages and placeholders). API errors carry a stable `key` and `args` (e.g. `password.tooShort`, `{min: 10}`) which the UI translates; the API's English message is only a fallback. Dates and numbers use `Intl` for the selected language. To add a language: add `<lang>.json`, list it in `LANGUAGES` (`i18n/index.ts`), and the test tells you what is missing.

### GraphQL types are generated
Nothing GraphQL is typed by hand. `schema.graphql` is exported from the API (`mise run schema:export`) and, with the operations in `src/frontend/graphql/*.graphql`, GraphQL Code Generator writes `src/frontend/gql/generated.ts`. After changing the API schema or a `.graphql` document run `mise run codegen` and commit the results; `mise run test` (and an API test) fail if they are stale.

## Test data (seeder)

`Tankstat.Seeder` is a standalone console tool that does only one thing: **deletes the database, creates it, migrates it and fills it with random, consistent test data**. For the no-authentication setup only: it creates no users (everything belongs to the anonymous owner) and refuses to run when `Auth__Mode` is anything but `None`.

```sh
mise run seed                                                       # 25 vehicles, 20 refuelings each, into the dev:api database
mise run seed -- --vehicles 500 --trashed 40 --refuelings 5-60 --seed 7
mise run seed -- --help
```

| Option | Meaning |
| --- | --- |
| `--vehicles <n>` | vehicles to create (default 25) |
| `--trashed <n>` | additional vehicles that are in the trash (default 0) |
| `--refuelings <n\|a-b>` | refuelings per vehicle, a number or a range (default 20) |
| `--recurring <n\|a-b>` | recurring expenses per vehicle, at most 5 (default 0-2) |
| `--notifications <n>` | notifications about access changes (default 15) |
| `--seed <n>` | same seed, same data (default 1234) |
| `--provider`, `--connection` | override the database (otherwise `Database__*` settings; `mise run seed` targets the `dev:api` SQLite file) |
| `--uploads <folder>` | the folder of uploaded pictures, which is deleted too because nothing would point to the pictures any more (default: `Storage:Path` if configured) |
| `--yes` | skip the "type yes" confirmation before the database is deleted |

The fuel logs are consistent: per vehicle, dates and the odometer only increase, the last fill-up is recent, litres follow the distance at a per-vehicle consumption (now and then a fill-up was not logged: the odometer runs on and the next log is marked), and prices drift slowly. Trashed vehicles keep their fuel logs.

Recurring expenses (insurance, vignette, inspection, oil change, tyres; the distance ones only on vehicles with a reading) are last done so that some are overdue, some due soon and the rest upcoming; the app turns the first two into reminders when the notifications are looked at, as it always does. The access-change notifications are seeded directly for the anonymous user (there is nobody to share with without authentication), over the last two weeks, about made-up people and the first live vehicles: about a third already read, a few standing for several changes, and one "more changes" digest when there are at least ten.

## Data model and migrations

A vehicle fuel log: `Vehicle` (name, licence plate, fuel type, units, optional picture) and `Refueling` (date, volume, a `Cost`, an `OdometerReading`, full tank, whether a fill-up before it was missed, optional note, who logged it). `OdometerReading`, `Cost` and the units are shared building blocks meant to be reused by later entities (inspections, service fees). Exposed via GraphQL (`vehicles`, `vehicle(id)`, `refuelings(vehicleId, ...)`, `refuelingTrash`, `vehicleLogAccess`, `shareCandidates`, `logDefaults`; mutations such as `addVehicle`, `logRefueling`, `updateRefueling`, `deleteRefueling`, `restoreRefueling`, `setVehicleLogAccess`, `emptyTrash`, `emptyRefuelingTrash`).

Pending migrations are applied automatically at startup. Each provider has its own migration project, so after changing an entity run:

```sh
mise run db:migration AddSomething   # adds the migration to all four providers
```

A unit test fails if any provider's migrations are out of sync with the model.

## Development

Run both in separate terminals:

```sh
mise run dev:api   # API with hot reload on http://localhost:5080 (GraphQL IDE at /graphql)
mise run dev:web   # Vite dev server, proxies /graphql and /auth to :5080
```

## Testing

```sh
mise run test              # typecheck, lint, then all test layers
mise run test:unit         # frontend + backend unit tests
mise run test:integration  # frontend (msw-mocked HTTP) + backend (in-process)
mise run test:api          # contract tests against a freshly started server
mise run test:coverage     # unit + integration tests with coverage reports in ./coverage
```

The API tests pin the GraphQL schema the frontend relies on, so a breaking schema change fails them. They read the target from `TANKSTAT_API_URL` (default `http://localhost:5080`); `test:api` sets it for you.

Frontend tests are named `*.unit.test.ts(x)` (a function or component on its own) or `*.integration.test.ts(x)` (the app against msw-mocked GraphQL); other names are not picked up. They are grouped by feature area, unit and integration tests side by side, nested where an area has parts:

```text
tests/frontend/
  support/                   shared helpers: msw default handlers (server.ts), fake backends (mocks.tsx), setup.ts
  app/  app/apollo/  app/i18n/   the shell, error handling, accessibility, Apollo, translations
  auth/  pwa/                sign-in, installing the app
  home/                      the home page and its vehicle cards
  vehicles/                  vehicle list and page; vehicles/logs/, vehicles/recurring/, vehicles/dashboard/ for its tabs
  recognition/  pictures/    photo reading, pictures and uploads
  trash/  grid/  settings/  notifications/  admin/  import/
```

A new test goes into the folder of the feature it covers; run one file with `npx vitest run tests/frontend/<area>/<file>`.

## Docker

One image contains everything: the .NET API serves GraphQL and the built web app, so a single container is the whole service. It is built from the `Dockerfile` (multi-stage: Node builds the frontend, the .NET SDK publishes the API, an Alpine ASP.NET runtime runs it as an unprivileged user).

```sh
mise run docker:build            # or: docker build -t tankstat --build-arg VERSION=1.2.3 .
docker run -d --name tankstat -p 8080:8080 -v tankstat-data:/data \
  -e Auth__Mode=Standalone -e Auth__Standalone__AdminEmail=admin@example.com -e Auth__Standalone__AdminPassword='a long password' \
  tankstat
```

- The app listens on port 8080 and is configured only through the environment variables described above (`Database__*`, `Auth__*`, `Smtp__*`). Without `Auth__Mode` the image uses whatever `appsettings.json` was built in, so always set it explicitly (and never bake credentials into `appsettings.json`).
- SQLite and the uploaded pictures live in the `/data` volume (`Database__ConnectionString=Data Source=/data/tankstat.db` and `Storage__Path=/data/uploads` by default); mount a volume to keep your data. Use `Database__Provider` and `Database__ConnectionString` for PostgreSQL, SQL Server or MySQL instead.
- Migrations are applied when the container starts. The container has a health check (the GraphQL endpoint).
- `VERSION` (build argument) is what the UI shows as the API version; `REVISION` and `CREATED` become OCI image labels.
- Terminate TLS in front of the container with your reverse proxy (see the ProxyHeader mode for proxy-based sign-in). Installing the web app on a phone, and the service worker behind it, need that HTTPS; the API serves `/manifest.webmanifest` and `/sw.js` from `wwwroot` with the cache rules described under *Install as an app*, and `scripts/docker-smoke.sh` checks them.
- The image builds for amd64 and arm64 with buildx without emulation (`docker buildx build --platform linux/amd64,linux/arm64 .`).

### Releases (container registry)

`.github/workflows/release.yml` publishes the image to the GitHub Container Registry as `ghcr.io/<owner>/<repo>`. Create a GitHub release with a tag like `v1.2.3` (or `v1.2.3-rc.1`): the workflow first runs the whole CI as a gate, builds the image, starts it and checks it with `scripts/docker-smoke.sh` (web app, API, version, database, non-root), and only then pushes a multi-architecture (amd64 + arm64) image with a provenance attestation and an SBOM. A release `v1.2.3` is tagged `1.2.3`, `1.2` and `1` (`latest` for stable releases; pre-releases only get their full version; `0.x` releases get no bare major tag). "Run workflow" on the Release workflow builds and smoke-tests from any branch and pushes a `dev-<sha>` image only if you tick *push*.

```sh
docker pull ghcr.io/<owner>/<repo>:1.2.3
mise run docker:build && mise run docker:smoke      # the same smoke test, locally
```

The first publish creates the package as private and linked to the repository; change its visibility in the package settings if the image should be public.

## Reading with an OpenAI-compatible model

Photo reading is optional and off by default. With it on, the app sends each photo picked in the add dialogs to a vision model behind an **OpenAI-compatible chat completions API**, which reads the odometer from a dashboard (seven-segment displays included) or the total, litres, price per litre, currency and date from a receipt (Hungarian, English and German receipts). The server can be one on your own network, which keeps the photos at home: [LM Studio](https://lmstudio.ai) (`http://host:1234/v1`), [Ollama](https://ollama.com) (`http://host:11434/v1`), llama.cpp's `llama-server` (`http://host:8080/v1`) or vLLM (`http://host:8000/v1`), with a model loaded that takes images (Qwen2.5-VL, Gemma 3, Llama 3.2 Vision and the like). It can also be a paid API such as OpenAI's (`https://api.openai.com/v1`, with a key), **which then receives every photo your users pick**: receipts show shops, times and the end of a card number, dashboards show the car. Choose that knowingly and tell your users. Only the app's own server talks to the model server; the browser never does.

```yaml
services:
  tankstat:
    image: ghcr.io/<owner>/<repo>:1.2.3
    environment:
      Recognition__Provider: OpenAiCompatible
      Recognition__OpenAiCompatible__BaseUrl: http://ollama:11434/v1
      Recognition__OpenAiCompatible__Model: qwen2.5vl:7b
      Recognition__MaxConcurrent: "1" # one GPU reads one photo at a time
```

| Setting | Environment variable | Meaning |
| --- | --- | --- |
| `Recognition:Provider` | `Recognition__Provider` | `None` (default: photos are not read) or `OpenAiCompatible` |
| `Recognition:OpenAiCompatible:BaseUrl` | `Recognition__OpenAiCompatible__BaseUrl` | the API's address including its version, e.g. `http://localhost:1234/v1` (the app adds `/chat/completions` and `/models`) |
| `Recognition:OpenAiCompatible:Model` | `Recognition__OpenAiCompatible__Model` | **required**: the model's name as `GET /v1/models` lists it (Ollama's `name:tag`, LM Studio's model id, a paid API's model name; a llama.cpp server with one model answers with it whatever is asked) |
| `Recognition:OpenAiCompatible:ApiKey` | `Recognition__OpenAiCompatible__ApiKey` | sent as `Authorization: Bearer` when set; servers on your own network usually need none |
| `Recognition:OpenAiCompatible:SystemPrompt` | `Recognition__OpenAiCompatible__SystemPrompt` | your own instructions to the model instead of the built-in ones (below) |
| `Recognition:OpenAiCompatible:SystemPromptFile` | `Recognition__OpenAiCompatible__SystemPromptFile` | the same from a file (e.g. `/data/prompt.txt`, at most 16 KB; read at startup, so restart after changing it); wins over `SystemPrompt` |
| `Recognition:OpenAiCompatible:ResponseFormat` | `Recognition__OpenAiCompatible__ResponseFormat` | how the shape of the answer is enforced: `JsonSchema` (default: OpenAI, LM Studio, Ollama, llama.cpp, vLLM), `JsonObject` (older servers; LM Studio rejects it) or `None` (the JSON is picked out of the text) |
| `Recognition:OpenAiCompatible:Temperature` | `Recognition__OpenAiCompatible__Temperature` | sent only when set (0 to 2): `0` makes a local model read the same digits the same way every time; some paid models refuse it |
| `Recognition:OpenAiCompatible:TimeoutSeconds` | `Recognition__OpenAiCompatible__TimeoutSeconds` | how long one photo may take before the attempt counts as failed and is tried again later (default `120`: a model on a CPU, or one Ollama has to load first, takes a while) |
| `Recognition:OpenAiCompatible:LogTraffic` | `Recognition__OpenAiCompatible__LogTraffic` | writes every request to the model server and every answer to the log, to see what the model is sent and says (default `false`; [details](#seeing-what-is-sent-to-the-model)) |
| `Recognition:MinConfidence` | `Recognition__MinConfidence` | values the model is less sure of are not filled in (0 to 1, default `0.6`) |
| `Recognition:MaxConcurrent` | `Recognition__MaxConcurrent` | photos read at the same time (default `2`; `1` for a single local GPU) |

Settings that cannot be used turn photo reading off with a warning in the log; they never stop the app. Photos are queued as they are uploaded and read by a background worker; a busy or unreachable model server is tried again later (five attempts), so nothing is lost while it restarts. What was read is kept with the photo (the values only, never another copy of the picture) and goes away with it.

A model rates its own answers, but those ratings are not to be trusted on their own, so the app checks what it can before believing a value: an odometer may not be below the vehicle's latest reading nor more than 100 000 above it, litres × price per litre must fit the total, a receipt's date must be recent and not in the future, and amounts must be within reason. A value that fails is dropped rather than filled in (a blank beats a wrong value); one that is merely unlikely (an odometer 50 000 to 100 000 above the latest reading, litres and price that do not fit the total) stays below the default fill-in threshold (0.5 against 0.6); and the model's own rating only ever lowers the result (one given in per cent is read as such). A refusal fails the photo; an answer that is not the JSON asked for, a cut-off one, or a server that is busy or unreachable is tried again (five attempts, the reason in the log).

**What the model is told.** Two texts go with each photo. The *system prompt* says how to read a dashboard or a receipt; replace it with `SystemPrompt` or `SystemPromptFile` when your photos need other guidance (a language the built-in one does not mention, a cluster it keeps misreading). The *contract* goes with the photo whatever the prompt and cannot be changed: what this photo may show (an odometer, or the receipt of the entry being added), the exact JSON shape and value formats, and how numbers and dates are written in the language the user works in (the dialog's language: Hungarian, German or English). So a custom prompt changes how the model reads, never what the app gets back. The vehicle's latest odometer reading, its usual currency and today's date are deliberately kept from the model (given a number, a model tends to answer with it); the app uses them to check the answer instead. The built-in system prompt is:

> You read photos for a vehicle fuel log and answer in JSON only. A photo shows one of two things: a car's instrument cluster, or a receipt.
>
> Dashboards. Read the odometer: the total distance the car has driven, a whole number of 4 to 7 digits, usually labelled km or mi (or ODO) and shown on the small display between the gauges or at the bottom of the cluster. It is not the trip meter (a smaller number with one decimal, labelled trip, A or B), not a service countdown ("Oil change and inspection in 10100 km", "4800 km múlva"), not a distance or consumption since start, not the clock, the date, the outside temperature, the speed or the fuel range. If the display shows no total distance, say the photo shows no odometer. Many displays use seven-segment digits: read each digit on its own and watch the pairs that look alike (0 and 8, 6 and 8, 1 and 7, 5 and 6, 3 and 9). Give the number exactly as printed, without spaces or units.
>
> Fuel receipts. Read the total paid (the final amount after discounts, often the largest number, labelled TOTAL, Összesen, Fizetendő, Summe or Gesamt), the volume of fuel (litres or gallons, usually with 2 or 3 decimals next to the fuel's name: Diesel, 95, E10, benzin, gázolaj, Super), the price per litre, the currency and the date of purchase. Volume times unit price should equal the total; if they do not, re-read them before answering.
>
> Other receipts. Read the total paid, the currency, the date and the shop's name as the title (the name printed at the top, not its address or tax number).
>
> Rules. Report only what you can actually read on the photo. Leave a value out rather than guess, and never invent a value that is not printed. Rate each value with a confidence between 0 and 1: 1 when it is clearly legible and unambiguous, about 0.8 when it is readable but small or partly blurred, under 0.6 when you had to guess. Read numbers and dates in the conventions of the receipt's language (a comma may be the decimal separator) and convert them to the output format you are asked for.

**What the model is sent.** One `POST {BaseUrl}/chat/completions` per photo. The body has this shape (`temperature` only when `Temperature` is set; no token limit is sent, the timeout bounds a runaway answer):

```json
{
  "model": "<Recognition:OpenAiCompatible:Model>",
  "stream": false,
  "messages": [
    { "role": "system", "content": "<the system prompt: the built-in one above, or yours>" },
    { "role": "user", "content": [
      { "type": "image_url", "image_url": { "url": "data:image/jpeg;base64,<the photo>" } },
      { "type": "text", "text": "<the contract, below>" }
    ] }
  ],
  "temperature": 0,
  "response_format": { "type": "json_schema", "json_schema": { "name": "photo_reading", "strict": true, "schema": { "...": "the schema, below" } } }
}
```

The photo comes first, then the contract, in one user message. The photo is the browser's resized copy (at most 1600 px on the longer side), as JPEG while reading is on and as WebP while it is off. `ResponseFormat=JsonObject` sends `"response_format": { "type": "json_object" }` instead and `None` sends none, so the contract alone says what the answer looks like. The contract depends on the dialog the photo was picked in (the refuelling dialog expects an odometer or a fuel receipt, the expense and recurring-expense dialogs an odometer or a receipt) and on the language the user works in (it ends with how that language writes numbers and dates; another language gets no such sentence). The contract for a photo picked in the **refuelling** dialog, in Hungarian, is:

> This photo was taken for a refuelling entry, so it shows an odometer ("odometer"), a fuel receipt ("fuel-receipt") or neither ("unknown"). Answer with JSON only, in exactly this shape: {"kind": "...", "fields": [{"name": "...", "value": "...", "confidence": 0.0}]}. The fields of each kind: odometer: odometer. fuel-receipt: total, volume, unitPrice, currency, date. Leave out a field you cannot read; with "unknown" there are no fields. Formats: odometer as digits only, without unit or separators; total, volume and unitPrice with "." as the decimal separator, no thousands separators, no currency sign; currency as an ISO 4217 code (HUF, EUR, USD); date as yyyy-MM-dd; title as the shop's name, at most 120 characters. Every value is a string. The receipt is probably Hungarian: numbers use a comma as the decimal separator and a space or a dot between thousands (1 234,5 or 1.234,5), and dates are written year first (2026.10.05.). Convert them to the formats above.

The contract for a photo picked in the **expense** dialogs, in English, is:

> This photo was taken for an expense entry, so it shows an odometer ("odometer"), a receipt ("expense-receipt") or neither ("unknown"). Answer with JSON only, in exactly this shape: {"kind": "...", "fields": [{"name": "...", "value": "...", "confidence": 0.0}]}. The fields of each kind: odometer: odometer. expense-receipt: total, currency, date, title. Leave out a field you cannot read; with "unknown" there are no fields. Formats: odometer as digits only, without unit or separators; total, volume and unitPrice with "." as the decimal separator, no thousands separators, no currency sign; currency as an ISO 4217 code (HUF, EUR, USD); date as yyyy-MM-dd; title as the shop's name, at most 120 characters. Every value is a string. The receipt is probably in English: numbers use a dot as the decimal separator and a comma between thousands (1,234.5); a date may be day/month/year or month/day/year, decide from the other clues on the receipt. Convert them to the formats above.

The schema of `ResponseFormat=JsonSchema`, for the refuelling dialog (the expense dialogs allow `expense-receipt` instead of `fuel-receipt` as the kind and `odometer`, `total`, `currency`, `date`, `title` as the names):

```json
{
  "type": "object",
  "additionalProperties": false,
  "required": ["kind", "fields"],
  "properties": {
    "kind": { "type": "string", "enum": ["odometer", "fuel-receipt", "unknown"] },
    "fields": {
      "type": "array",
      "items": {
        "type": "object",
        "additionalProperties": false,
        "required": ["name", "value", "confidence"],
        "properties": {
          "name": { "type": "string", "enum": ["odometer", "total", "volume", "unitPrice", "currency", "date"] },
          "value": { "type": "string" },
          "confidence": { "type": "number" }
        }
      }
    }
  }
}
```

**How the answer is read.** The first `{` to the last `}` of what the model said is the JSON (a reasoning block, code fences or a sentence around it are ignored; numbers are accepted for values; names are matched whatever their spelling, `unit_price` or `Unit Price` included). A value is only filled in when its `confidence` (0 to 1) is at least `Recognition:MinConfidence` after the checks above; a missing or unusable confidence counts as 0.5.

**Replaying a photo by hand** (in Open WebUI, say): put the system prompt in the chat's system prompt, attach the photo and paste the contract into the same message. What can still make an answer differ from the app's: the photo (click a thumbnail in the add dialog and save it: that is the resized copy that is sent, a phone's original is a different picture to the model), the `response_format` (a chat interface usually sends none: try `Recognition__OpenAiCompatible__ResponseFormat=None` to compare), the temperature, and any system prompt or parameters the chat interface adds on its own.

**Following a photo through the log.** With `Logging__LogLevel__Tankstat=Debug` every photo leaves a trail:

```
Asking qwen2.5-vl at http://lmstudio:1234/v1 to read photo 3f2c0a6e-... (image/jpeg, 231 KB)
qwen2.5-vl answered photo 3f2c0a6e-... in 3412 ms (HTTP 200, finish stop, 1534+96 tokens): said Odometer, listed 1, kept [], issues [Odometer:OdometerBelowLatest]
Read photo 3f2c0a6e-...: it shows Odometer, 0 of 0 values are sure enough to be filled in (rated 0.6 or more)
```

`listed` is how many values the model gave, `kept` the ones that passed the checks with their confidence, and `issues` what became of the others, as `Field:Reason` (or just the reason when it concerns the whole photo). Never the values themselves.

| Reason | Meaning |
| --- | --- |
| `Unrecognised` | the model took the photo for something this entry has no use for, or for nothing (`unknown`) |
| `NothingLegible` | the model knew what the photo shows but gave no value it could read |
| `NotUnderstood` | the value is not written the way the app takes it: an odometer must be digits only (`123 456`, `123.456` or `123456 km` are dropped), a date `yyyy-MM-dd`, an amount a plain number |
| `OutOfRange` | an amount no fill-up, price or total can be |
| `OdometerBelowLatest` | the odometer is lower than the vehicle's latest logged reading (it never goes back): dropped |
| `OdometerTooFarAbove` | more than 100 000 above the latest reading: dropped |
| `OdometerFarAbove` | 50 000 to 100 000 above it: kept, but at 0.5, under what is filled in |
| `DateTooOld`, `DateInFuture` | a receipt date more than three years back, or after tomorrow: dropped |
| `AmountsDoNotAdd` | litres times price do not fit the total: litres and price are kept at 0.5, the total stays |
| `NoConfidence` | the model gave the value no usable confidence: it counts as 0.5 |
| `Unsure` | the model rated the value under `Recognition:MinConfidence` (worked out when the reading is listed, so it follows the setting) |

The add dialogs say the same in the user's language (English or Hungarian): when a photo gave less than it might have, a notice next to what was filled in, or instead of it, says why ("The photo shows an odometer reading that is lower than the last one logged (300 000 km), so it was not filled in."), for the fields the dialog has. The reasons are kept with the reading as codes, never the value, and the `photoDrafts` query returns them (`reading { values issues { code field } }`), which is what the browser's network tab shows while you try a photo.

Notes on servers. Ollama loads a model on the first request after a while idle (keep it loaded with `OLLAMA_KEEP_ALIVE`, or allow a longer `TimeoutSeconds`) and runs it with a short context by default, which a photo can exceed (`OLLAMA_CONTEXT_LENGTH=8192` or more). llama.cpp's server needs the model's projector (`--mmproj`) to take images. A reverse proxy in front of the server must accept request bodies of a few megabytes (a photo travels as base64). The health check is `GET /v1/models`: the key must be allowed to list models (an OpenAI restricted key needs "Models: Read"), though a server that answers 403 to it is taken as available and judged on the first read. While reading is on, photos picked in the add dialogs are uploaded as JPEG rather than WebP, which not every server decodes. The dialog waits up to about two minutes for a reading and says so next to Save, with a note that you can save right away; a slower one lands on the saved entry later, marked for review. Measure a model on your own photos before trusting it: a wrong value the model is sure of is the one thing the checks cannot always catch.

### Seeing what is sent to the model

The trail above says what became of each value, not what the model said. For that, set `Recognition__OpenAiCompatible__LogTraffic=true` (it needs no Debug level). Every request to the model server and every answer is then written to the log at `Information`, one entry each, pretty-printed, with a number that pairs them and the photo's id (`health check` for the call that asks which models there are):

```
Model request 3 (photo 3f2c0a6e-...): POST http://lmstudio:1234/v1/chat/completions
Authorization: Bearer ***
Content-Type: application/json; charset=utf-8
{
  "model": "qwen2.5-vl",
  "stream": false,
  "messages": [
    { "role": "system", "content": "You read photos for a vehicle fuel log ..." },
    { "role": "user", "content": [ { "type": "image_url", "image_url": { "url": "data:image/jpeg;base64,[231 KB]" } }, ... ] }
  ],
  ...
}
Model answer 3 (photo 3f2c0a6e-...): HTTP 200 after 3412 ms
Content-Type: application/json
{ "choices": [ { "message": { "content": "{\"kind\":\"odometer\",\"fields\":[...]}" }, "finish_reason": "stop" } ], ... }
```

**These entries hold what no other line of the app does: the prompts and what the model read** (amounts, odometer readings, dates, shop names). The photo itself is only written as its size, the key is masked (the `Authorization` header, and the key itself wherever a server repeats it in an answer) and a body is cut after 16 000 characters. A server's own text cannot add a line to the log: line breaks in an entry come from its layout only, so with `Logging__Console__FormatterName=json` each entry stays one line for a log collector. The app warns at start while the setting is on. Switch it on while you investigate and off afterwards, since whoever collects your logs keeps them. In Kubernetes it is one more entry in the container's `env:` (`name: Recognition__OpenAiCompatible__LogTraffic`, `value: "true"`); `kubectl logs deploy/<name> -f` then shows each photo as it is read. The model server's own log shows the same from its side (LM Studio's developer log, `OLLAMA_DEBUG=1` for Ollama, `--verbose` for llama.cpp).

## Continuous integration

`.github/workflows/ci.yml` runs on every push to `main` and every pull request, installing the toolchain from `mise.toml` so CI uses the same Node and .NET versions as developers. Jobs run in parallel: **frontend** (lint, typecheck, Vitest, production build), **backend** (build, unit and in-process integration tests), **smoke** (the real app as a process in every authentication mode, plus the black-box API contract tests), **codegen** (`schema.graphql` and the generated GraphQL types are up to date). When all pass, **build** uploads the self-hostable output (`out/`) as an artifact. Run the same things locally with `mise run test`.

**Coverage.** The frontend job runs Vitest with V8 coverage (`lcov`) and the backend job runs the unit and integration tests with coverlet (`coverlet.runsettings`: the app's own code, without migrations, the seeder and the tests; Cobertura output). Both upload to [Codecov](https://codecov.io/gh/carathorys/tankstat) under the flags `frontend` and `backend`; `codecov.yml` holds the gates (project coverage may not drop by more than 1% against the base, new and changed lines need 80%) and the pull request comment. The upload needs the Codecov GitHub app and a `CODECOV_TOKEN` repository secret (`release.yml` passes secrets on to `ci.yml`); without the token the step only warns. `mise run test:coverage` writes the same reports to `coverage/` (HTML for the frontend at `coverage/frontend/index.html`).

## Building and self-hosting

```sh
mise run build
dotnet out/Tankstat.Api.dll --urls http://0.0.0.0:8080
```

`out/` contains the published API with the built frontend in `wwwroot`, including the web app's `manifest.webmanifest`, `sw.js` and `workbox-*.js`. Unknown paths fall back to `index.html` for client-side routing (the service worker does the same in the browser, never for `/graphql`, `/auth`, `/media` or `/imports`); `/assets/*` is sent as immutable, everything with a fixed name as `no-cache`.
