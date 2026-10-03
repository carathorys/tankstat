# Tankstat

[![CI](https://github.com/carathorys/tankstat/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/carathorys/tankstat/actions/workflows/ci.yml)
[![Release workflow](https://github.com/carathorys/tankstat/actions/workflows/release.yml/badge.svg)](https://github.com/carathorys/tankstat/actions/workflows/release.yml)
[![codecov](https://codecov.io/gh/carathorys/tankstat/graph/badge.svg)](https://codecov.io/gh/carathorys/tankstat)
[![Latest release](https://img.shields.io/github/v/release/carathorys/tankstat?include_prereleases&sort=semver)](https://github.com/carathorys/tankstat/releases)
[![License: MIT](https://img.shields.io/github/license/carathorys/tankstat)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Node LTS](https://img.shields.io/badge/Node-LTS-339933?logo=nodedotjs&logoColor=white)
![TypeScript](https://img.shields.io/badge/TypeScript-3178C6?logo=typescript&logoColor=white)

Self-hosted web app: a React + TypeScript frontend (Vite) and a .NET 10 backend. They talk GraphQL (HotChocolate on the server, Apollo Client in the browser), and the backend stores data with Entity Framework Core. In production, Kestrel serves both the API and the built frontend.

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
tests/frontend/                           Vitest unit (*.unit.test.*) and integration (*.integration.test.*) tests
scripts/test-api.sh                       starts the API, runs the API tests, stops it
src/backend/Tankstat.Reader.Core/         optional photo reader: OCR text model, number/date/currency parsing, reading rules (no native dependencies)
src/backend/Tankstat.Reader/              photo reader service: HTTP API, image preparation (SkiaSharp), Tesseract, generated photos, evaluation tool
tests/backend/Tankstat.Reader.Core.UnitTests/     reading rules on hand-built OCR pages, contract shape, project boundaries
tests/backend/Tankstat.Reader.IntegrationTests/   reader HTTP API, image preparation, generator and evaluation, the real Tesseract (when installed)
tests/backend/contracts/reader/           examples of the reader's HTTP contract
```

The photo reader is a separate service with its own image (see [Photo reader](#photo-reader-optional-service)); it references no app project and the app references none of it: the app talks to it over HTTP through its `IRecognitionProvider` port.

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

The app is mobile-first and responsive. The start page (*Home*) shows a card per vehicle you own or that is shared with you (a search box finds vehicles by name or license plate, the server does the searching, and more cards load as you scroll to the end of the list; a *Show more* button does the same without scrolling), and is where you add vehicles. Each card shows: its picture as the background (a gradient without one), plate and fuel type, a badge when the vehicle is shared with you, the latest odometer, average consumption, last fill-up, this month's spending and a small six-month trend, and what is overdue or due soon among the vehicle's recurring expenses. A navigation menu opened by the hamburger button in the top bar gives access to:

- **Vehicles** (administrators only; never available with `Auth:Mode=None`, where there is no administrator): the grid of all vehicles of everyone, sorted and paged by the server, with edit and delete. Everyone else sees and manages their own vehicles on *Home* (editing and deleting appear for vehicles you may edit).
- **Vehicle page** (tabs; a banner with the vehicle's picture on top): **Dashboard** (key figures, ready-made charts and your own charts), **Refuelings** (the logs: add, edit, move to trash; sortable, paged, selectable columns), **Expenses** (service, insurance, parking, ...: title, free-text category, a cost with its own currency, optional odometer; same access rules as the logs), **Details** (owner, fuel, units, the vehicle's picture) and **Sharing** (give people access to this vehicle's logs; only for those who can edit the vehicle).
- **Import**: bring fuel logs, other costs and recurring expenses from another app (Fuelio CSV today), see below.
- **Notifications** (also the bell in the top bar, which shows how many are unread and the latest ones): what you were told, newest first; mark one or all read. See *Notifications* below.
- **Trash**: deleting moves a vehicle, a refuelling or an expense to the trash. Tabs *Vehicles*, *Refuelings* and *Expenses*: **restore**, or **empty the trash**, which permanently deletes only what you have Delete access to (the rest stays and the dialog says so).
- **Account** (profile picture; change password in Standalone mode) and **Administration** (administrators only), plus **Sign out**.

**Recurring expenses.** The *Recurring* tab of a vehicle holds schedules for things that come back: insurance every 12 months, an oil change every 15,000 km or every 12 months, whichever comes first. A schedule repeats by **time**, by **distance (odometer)** or **combined** (whichever is reached first), counts from the day (and odometer) it was last done, and shows when it is next due and whether it is *Upcoming*, *Due soon* (within the warning time and distance of the item; a new schedule starts from `Defaults:RecurringWarnDays` / `Defaults:RecurringWarnDistance`, 30 days and 500 distance units unless configured) or *Overdue*. The current odometer is the vehicle's latest reading from any log; without one a distance-based schedule cannot be judged yet. **Mark as done** starts the next interval from the day and odometer you enter and, unless you switch it off, logs the cost as a normal expense (so every expense and odometer rule applies); photos taken there (the dashboard, the invoice) become that expense's photos. Schedules follow the access rules of the vehicle's logs (View sees them, Edit changes them) and are deleted for good, not trashed; deleting a user hands them on or removes them like the vehicle's other data. What is overdue or due soon is listed on the vehicle's card on *Home*.

**Notifications.** You are notified when someone gives you access to a vehicle's logs, changes or takes it away; when someone other than you shares *your* vehicle; when an administrator gives you (or someone, on your data) access to everything a user owns; when an administrator changes the default access for everyone; and when a recurring expense of a vehicle whose logs you may see becomes due soon or overdue (an administrator only for vehicles that concern them personally, not for every vehicle on the instance). Recurring reminders are worked out when you look (the bell asks every minute while the page is open), once per cycle of the schedule: one that turns from due soon to overdue is the same notification again, unread, and marking it done starts a new cycle. To avoid floods, changes to the same thing are folded into the unread notification about it ("3 changes"), a change undone before you read it disappears, and past `Notifications:MaxPerHour` notifications in an hour further ones are only counted in one "more changes" notification. You cannot delete notifications: you mark them read (when is kept), and the system removes them `Notifications:ReadRetentionDays` after that (checked when you look at them; a reminder of a schedule that is still due stays). Nobody else, administrators included, can see yours. With `Auth:Mode=None` they work for the single anonymous user.

**Navigation menu.** On a desktop it is a docked sidebar, open by default; the hamburger button hides and shows it, and the choice is remembered in this browser only (`localStorage`, `tankstat.nav.open`). On a phone it is an overlay drawer, closed until the button is pressed, and closes after choosing a page.

**Importing.** The *Import* page walks through four steps: choose the file, choose the target (an existing vehicle you may add logs to, or a new one), review, done. Nothing is saved before you confirm.

- The file is uploaded raw with `POST /imports/{format}` (the answer is a token for the parsed file, kept in memory for 30 minutes); the preview (`importPreview`) and the confirmation (`confirmImport`) are GraphQL.
- **Fuelio** (`fuelio`): the "sync" CSV export of one vehicle (unzip it first). Fuel logs, other costs (with their category names) and the vehicle's name, plate, units and fuel type are read; reminder templates that repeat (every so many months and/or kilometres) become recurring expenses, counting from where the reminder stands now (their amount is not kept; one-off reminders are skipped), stations, GPS and weather are ignored, income rows are skipped, and a cost with odometer 0 gets no odometer. Fuelio files carry no currency, so you enter one (it defaults to `Defaults:Currency`). Dates keep only the day.
- Rows are saved through the same services as rows typed by hand, so every rule applies (access, odometer order against the vehicle's other readings, future dates). A row that breaks a rule is reported and the rest is still imported. For an existing vehicle you choose whether rows that already exist (same date and odometer; same date, title and amount for expenses) are skipped or imported again.
- More formats (other CSV or JSON sources) are one more `IImportParser` that turns a file into an `ImportBatch`.

**Dashboard and charts.** The first tab of a vehicle shows key figures (spent this month, average consumption of the last ten full fill-ups, odometer, last fill-up), five ready-made charts (monthly costs split into fuel and expenses, consumption, fuel price, expenses by category, distance per month) and the charts people composed. *Add chart* opens a builder: what to measure (total spend, fuel cost, expense cost, fuel volume, distance, average consumption, average fuel price, number of fill-ups), how to group it (month, quarter, year, or expense category for expense costs), the chart type (bars, line, area, donut) and the period: 1 month, 3 months, half a year, one year, this year, last year, all time, or a **custom from-to range**. Only valid combinations are offered, and a live preview shows your own data. A chart is private to its creator unless someone with edit access to the vehicle's logs shares it with everyone who sees the vehicle; only the creator changes it (anyone with delete access may remove a shared one). The numbers are calculated by the server from the stored logs (costs per currency, never mixed), charts are drawn with Recharts, and every chart can be shown as a table (which is what screen readers read).

**Fuel consumption.** Each full fill-up shows the consumption since the previous full one: all fuel added since then (partial top-ups in between count, the previous full fill-up does not) divided by the distance driven, per 100 distance units, in the vehicle's own units (for example L/100 km; miles with gallons read as mpg). Partial fill-ups, the first full fill-up and logs without any distance since the last full one show a dash. It is stored on the log and refreshed for the whole vehicle whenever one of its logs is added, changed, trashed, restored or imported (once per import), never on read; the first start after the upgrade calculates it for existing logs. The column can be sorted on the server like the others.

**Units and money.** Every vehicle has its own distance unit (kilometres or miles) and fuel volume unit (litres, US gallons, imperial gallons); numbers are stored exactly as entered and never converted, so the units are locked once a vehicle has logs. Every cost carries its own currency (a reusable `Cost` value: amount plus currency), and a log links to an odometer reading (a reusable `OdometerReading`, without an upper limit; the server checks a new reading against the neighbouring readings of the same vehicle, from any source).

**Pictures.** Users can upload a profile picture (Account page; shown with the Radix `Avatar` wherever users appear) and a picture per vehicle (Details tab). The browser scales the picture down (and crops profile pictures square) and re-encodes it before it is sent; the server still checks the real file type (JPEG, PNG, WebP only; 2 MiB maximum). Pictures are served from `/media/{id}` (immutable, cached; only to signed-in users who may see the owner or vehicle) and uploaded with `PUT /media/me/avatar` and `PUT /media/vehicles/{id}/picture` (removed with `DELETE` on the same paths). These, the photo endpoints below and the import upload (`POST /imports/{format}`) are the only REST endpoints; everything else is GraphQL.

**Photos of refuelings and expenses.** Every refueling and expense can have up to 10 photos (receipts, the pump display, ...). In the add and edit dialogs, **Take photo** opens the phone's camera straight away and **Add photos** picks from the library; photos chosen while adding are uploaded straight away as drafts and attached when the entry is saved (so a failed save loses none; a photo that could not be uploaded is marked and can be tried again), and while editing they are uploaded or removed at once. Photos are scaled to 1600 px in the browser (location data in them is dropped), stored in the log's folder below the vehicle's (see `Storage:Path`) and follow the access rules of the vehicle's logs: whoever may see the log may see its photos, whoever may edit it may add and remove them. They are uploaded with `PUT /media/expenses/{id}/photos` or `PUT /media/refuelings/{id}/photos`, removed with `DELETE` on `.../photos/{imageId}` and shown through `/media/{id}`. Drafts are uploaded with `PUT /media/vehicles/{id}/photo-drafts` (needs edit access to the vehicle's logs), removed with `DELETE /media/photo-drafts/{id}`, attached through the `photoIds` of `logRefueling` / `addExpense`, seen only by their uploader, kept in `vehicles/<id>/drafts/` and removed after a day if no entry was saved with them. A photo disappears from view with its log in the trash and is deleted for good when the log or its vehicle is.

**Reading values from photos (optional).** With the [photo reader](#photo-reader-optional-service) set up, the server reads the photos picked in the add dialogs of refuelings and expenses and in *Mark as done*: a dashboard gives the odometer, a fuel receipt the date, litres, total and currency, another receipt the date, shop (as the title), amount and currency. While a photo is being read its thumbnail says *Reading…*; then the values go into the fields you have not changed (the starting values, such as today's date or the last currency, count as unchanged), marked *Read from the photo; check it*. Where you already typed something else, the photo's value is only offered (*The photo shows 38.52 · Use it*), never written over yours. Only values the reader is sure enough of are filled in (`Recognition:MinConfidence`). The page only talks to Tankstat's own server, which talks to the reader; without it the dialogs work exactly as before.

**Accessibility.** The UI is built to be keyboard- and screen-reader friendly: landmarks and a skip link, a labelled navigation, labelled form controls with linked hints and errors, announced upload/loading status, table semantics with `aria-sort`, 44 px touch targets, dialogs with focus management, and reduced-motion support. Automated axe checks run in the frontend integration tests; colour contrast should still be reviewed by eye. The look is dim and layered: translucent blurred panels (top bar, sidebar) with soft shadows.

### Grids
Sorting (click a column header), paging and column selection are done by the **server**: the GraphQL query gets `orderBy`, `direction`, `skip`, `take`, and one Boolean variable per optional column that drives `@include`, so hidden columns are not even fetched. The toolbar has a refresh button and a column picker (show/hide and move up/down, which works with touch). Column choices, order, page size and sorting are remembered per grid in the browser. Grid state is coordinated by TanStack Table (manual sorting and pagination: the data itself arrives sorted and paged from the server). Data is re-fetched whenever a page is opened. The layout is mobile-first: on a phone only the essential columns start visible, and the table scrolls horizontally.

### Languages
English and Hungarian (react-i18next); the language menu in the top bar is available before sign-in, defaults to the browser language and is remembered. Every visible string is in `src/frontend/i18n/locales/<lang>.json` (typed keys; a test checks that all languages have the same messages and placeholders). API errors carry a stable `key` and `args` (e.g. `password.tooShort`, `{min: 10}`) which the UI translates; the API's English message is only a fallback. Dates and numbers use `Intl` for the selected language. To add a language: add `<lang>.json`, list it in `LANGUAGES` (`i18n/index.ts`), and the test tells you what is missing.

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

The fuel logs are consistent: per vehicle, dates and the odometer only increase, the last fill-up is recent, litres follow the distance at a per-vehicle consumption, and prices drift slowly. Trashed vehicles keep their fuel logs.

Recurring expenses (insurance, vignette, inspection, oil change, tyres; the distance ones only on vehicles with a reading) are last done so that some are overdue, some due soon and the rest upcoming; the app turns the first two into reminders when the notifications are looked at, as it always does. The access-change notifications are seeded directly for the anonymous user (there is nobody to share with without authentication), over the last two weeks, about made-up people and the first live vehicles: about a third already read, a few standing for several changes, and one "more changes" digest when there are at least ten.

## Data model and migrations

A vehicle fuel log: `Vehicle` (name, licence plate, fuel type, units, optional picture) and `Refueling` (date, volume, a `Cost`, an `OdometerReading`, full tank, optional note, who logged it). `OdometerReading`, `Cost` and the units are shared building blocks meant to be reused by later entities (inspections, service fees). Exposed via GraphQL (`vehicles`, `vehicle(id)`, `refuelings(vehicleId, ...)`, `refuelingTrash`, `vehicleLogAccess`, `shareCandidates`, `logDefaults`; mutations such as `addVehicle`, `logRefueling`, `updateRefueling`, `deleteRefueling`, `restoreRefueling`, `setVehicleLogAccess`, `emptyTrash`, `emptyRefuelingTrash`).

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

The optional photo reader runs on its own (it needs `tesseract` with the `hun`, `eng` and `deu` languages installed):

```sh
mise run dev:reader   # http://localhost:5090, API key "dev"
```

## Testing

```sh
mise run test              # typecheck, lint, then all test layers
mise run test:unit         # frontend + backend unit tests
mise run test:integration  # frontend (msw-mocked HTTP) + backend (in-process)
mise run test:api          # contract tests against a freshly started server
mise run test:coverage     # unit + integration tests with coverage reports in ./coverage
mise run test:reader       # only the photo reader's unit + integration tests
```

The photo reader's tests that run the real Tesseract are skipped where `tesseract` is not installed (CI installs it).

The API tests pin the GraphQL schema the frontend relies on, so a breaking schema change fails them. They read the target from `TANKSTAT_API_URL` (default `http://localhost:5080`); `test:api` sets it for you.

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
- Terminate TLS in front of the container with your reverse proxy (see the ProxyHeader mode for proxy-based sign-in).
- The image builds for amd64 and arm64 with buildx without emulation (`docker buildx build --platform linux/amd64,linux/arm64 .`).

### Releases (container registry)

`.github/workflows/release.yml` publishes the image to the GitHub Container Registry as `ghcr.io/<owner>/<repo>`. Create a GitHub release with a tag like `v1.2.3` (or `v1.2.3-rc.1`): the workflow first runs the whole CI as a gate, builds the image, starts it and checks it with `scripts/docker-smoke.sh` (web app, API, version, database, non-root), and only then pushes a multi-architecture (amd64 + arm64) image with a provenance attestation and an SBOM. A release `v1.2.3` is tagged `1.2.3`, `1.2` and `1` (`latest` for stable releases; pre-releases only get their full version; `0.x` releases get no bare major tag). "Run workflow" on the Release workflow builds and smoke-tests from any branch and pushes a `dev-<sha>` image only if you tick *push*.

```sh
docker pull ghcr.io/<owner>/<repo>:1.2.3
mise run docker:build && mise run docker:smoke      # the same smoke test, locally
```

The first publish creates the package as private and linked to the repository; change its visibility in the package settings if the image should be public.

The same release publishes the optional [photo reader](#photo-reader-optional-service) as `ghcr.io/<owner>/<repo>-reader` (same version and tags, amd64 + arm64), after its own smoke test (`scripts/docker-smoke-reader.sh`: health with the three languages, the API key, a real reading of a receipt the image generates itself, non-root). Locally: `mise run docker:build:reader && mise run docker:smoke:reader`.

## Photo reader (optional service)

A separate service that reads values from photos: the odometer from a dashboard, the total, litres, price per litre, currency and date from a fuel receipt, and the total, currency, date and shop from other receipts (Hungarian, English and German). It reads with [Tesseract](https://github.com/tesseract-ocr/tesseract) and a set of rules, runs in its own container and needs nothing from the app. The app uses it to fill in the log dialogs (see *Reading values from photos* above), always from its own server: the browser never talks to the reader.

Run both containers on one private network and point the app at the reader (both images are built for amd64 and arm64):

```yaml
services:
  tankstat:
    image: ghcr.io/<owner>/<repo>:1.2.3
    ports: ["8080:8080"]
    volumes: [tankstat-data:/data]
    environment:
      Auth__Mode: Standalone
      Auth__Standalone__AdminEmail: admin@example.com
      Auth__Standalone__AdminPassword: a long password
      Recognition__Provider: Reader
      Recognition__Reader__BaseUrl: http://reader:8081
      Recognition__Reader__ApiKey: ${READER_API_KEY}
  reader:
    image: ghcr.io/<owner>/<repo>-reader:1.2.3 # no published port: only the app talks to it
    volumes: [reader-data:/data]
    environment:
      Reader__ApiKey: ${READER_API_KEY}
volumes:
  tankstat-data:
  reader-data:
```

The app's side (photo reading is off unless `Recognition:Provider` is set; settings that cannot be used turn it off with a warning in the log, they never stop the app):

| Setting | Environment variable | Meaning |
| --- | --- | --- |
| `Recognition:Provider` | `Recognition__Provider` | `None` (default) or `Reader` |
| `Recognition:Reader:BaseUrl` | `Recognition__Reader__BaseUrl` | the reader's address, e.g. `http://reader:8081` (a path below it works too, behind a proxy) |
| `Recognition:Reader:ApiKey` | `Recognition__Reader__ApiKey` | the key the reader was started with (its `Reader__ApiKey`) |
| `Recognition:Reader:TimeoutSeconds` | `Recognition__Reader__TimeoutSeconds` | how long one photo may take before the attempt counts as failed (default `30`) |
| `Recognition:MinConfidence` | `Recognition__MinConfidence` | values the reader is less sure of are not filled in (0 to 1, default `0.6`) |
| `Recognition:MaxConcurrent` | `Recognition__MaxConcurrent` | photos read at the same time (default `2`) |

Photos are queued as they are uploaded and read by a background worker; a busy or unreachable reader is tried again later (five attempts), so nothing is lost while it restarts. What was read is kept with the photo (the values only, never another copy of the picture) and goes away with it. The reader on its own:

```sh
docker run -d --name tankstat-reader -p 8081:8081 -v tankstat-reader-data:/data \
  -e Reader__ApiKey='a long random secret' ghcr.io/<owner>/<repo>-reader:1.2.3

curl http://localhost:8081/v1/health
curl -H 'X-Api-Key: a long random secret' -H 'Content-Type: image/jpeg' --data-binary @receipt.jpg \
  'http://localhost:8081/v1/read?kinds=fuel-receipt,odometer&locale=hu&currency=HUF'
```

The answer names what the photo shows and the values with how sure the reader is (`{"kind":"fuel-receipt","fields":[{"name":"total","value":"24669","confidence":0.93,"source":"ocr"}, ...]}`); a photo it cannot read is `"kind":"unknown"` with no fields. The examples in `tests/backend/contracts/reader/` are the contract (v1). Optional hints make the answers better: `kinds` (what may be on the photo), `locale` (`hu`, `en` or `de`: how numbers and dates are written), `lastOdometer` (the latest known reading: lower values, or ones more than 100 000 higher, are not taken), `currency` (when the receipt shows none) and `today` (receipt dates must be close to it).

| Setting | Environment variable | Meaning |
| --- | --- | --- |
| `Reader:ApiKey` | `Reader__ApiKey` | **required**: the secret every caller sends in the `X-Api-Key` header; the reader does not start without it |
| `Reader:Tesseract:Languages` | `Reader__Tesseract__Languages` | receipt languages in Tesseract's form (default `hun+eng+deu`; languages that are not installed are left out, `/v1/health` lists the rest) |
| `Reader:Tesseract:OdometerLanguages` | `Reader__Tesseract__OdometerLanguages` | the model for odometer digits (default `eng`) |
| `Reader:Tesseract:TessdataPath` | `Reader__Tesseract__TessdataPath` | a folder with other models (for example `tessdata_best`, mounted into the container); default: the installed ones |
| `Reader:Tesseract:TimeoutSeconds` | `Reader__Tesseract__TimeoutSeconds` | the longest a single Tesseract run may take (default `20`) |
| `Reader:MaxConcurrent` | `Reader__MaxConcurrent` | photos read at the same time (default half the CPU cores, 1 to 4) |
| `Reader:QueueLimit` | `Reader__QueueLimit` | photos that may wait for a free slot; beyond that the answer is `503` with `busy` (default `8`) |
| `Reader:MaxImageBytes` | `Reader__MaxImageBytes` | the largest photo accepted (default 8 MB) |
| `Reader:DataPath` | `Reader__DataPath` | where later versions keep what the reader learns (`/data` in the image) |

- **Privacy:** photos are read in memory and never stored; the logs name the kind, the number of values and the time taken, never a value or a picture. Only callers with the key get an answer: keep the reader on a private network next to the app rather than on the internet.
- The container listens on port 8081, runs as an unprivileged user and has a health check (it fails when Tesseract cannot run). The image is Ubuntu-based (Tesseract), unlike the app's Alpine image.
- **How well it reads:** on generated photos, totals, currencies and dates of receipts are right 97–100 % of the time and litres and prices about 95 %; odometers only about half the time, mostly the seven-segment displays (a trained digit model is planned). Real photos will do worse: measure your own (below). Every value comes with a confidence, and the app fills in only the ones the reader is sure of (`Recognition:MinConfidence`, 0.6 by default), leaving the rest to you.

**Measuring it.** The reader can draw photos with known values (receipts and dashboards with blur, tilt, glare and noise) and report how well it reads them, or read your own photos:

```sh
mise run reader:eval -- --synthetic 90            # generated photos, the same ones for the same --seed
mise run reader:synth -- eval/generated           # write generated photos and their expected values to a folder
dotnet run --project src/backend/Tankstat.Reader --no-launch-profile -- --init eval   # an empty <name>.expected.json next to every photo in eval/
mise run reader:eval -- eval --edge 1600 --verbose   # read them (shrunk like the browser does), show what the OCR saw
docker run --rm tankstat-reader:local --eval --synthetic 90   # the same in the image (its three languages read differently)
```

The report shows, per kind and value, how many were exact, close, wrong or missing, how sure the reader was of the right and the wrong ones, and how many the app would fill in. Keep real receipts in `eval/` (ignored by git): they never belong in the repository.

## Continuous integration

`.github/workflows/ci.yml` runs on every push to `main` and every pull request, installing the toolchain from `mise.toml` so CI uses the same Node and .NET versions as developers. Jobs run in parallel: **frontend** (lint, typecheck, Vitest, production build), **backend** (build, unit and in-process integration tests, including the photo reader's with a real Tesseract), **smoke** (the real app as a process in every authentication mode, plus the black-box API contract tests), **reader-image** (builds the photo reader image and runs its smoke test), **codegen** (`schema.graphql` and the generated GraphQL types are up to date). When all pass, **build** uploads the self-hostable output (`out/`) as an artifact. Run the same things locally with `mise run test`.

**Coverage.** The frontend job runs Vitest with V8 coverage (`lcov`) and the backend job runs the unit and integration tests with coverlet (`coverlet.runsettings`: the app's own code, without migrations, the seeder and the tests; Cobertura output). Both upload to [Codecov](https://codecov.io/gh/carathorys/tankstat) under the flags `frontend` and `backend`; `codecov.yml` holds the gates (project coverage may not drop by more than 1% against the base, new and changed lines need 80%) and the pull request comment. The upload needs the Codecov GitHub app and a `CODECOV_TOKEN` repository secret (`release.yml` passes secrets on to `ci.yml`); without the token the step only warns. `mise run test:coverage` writes the same reports to `coverage/` (HTML for the frontend at `coverage/frontend/index.html`).

## Building and self-hosting

```sh
mise run build
dotnet out/Tankstat.Api.dll --urls http://0.0.0.0:8080
```

`out/` contains the published API with the built frontend in `wwwroot`. Unknown paths outside `/graphql` fall back to `index.html` for client-side routing.
