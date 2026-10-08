# Development

For people who work on Tankstat itself: the toolchain, the layout of the repository, how to run and test it, and the tools around it.

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
src/frontend/                             React + Vite app: top bar, hamburger menu, pages (vehicles, trash, account, administration); UI built with MUI (@mui/material, with MUI X grid, charts and date pickers), lucide-react icons, motion animations, react-i18next
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

Photo reading is optional: the app talks to a vision model behind an OpenAI-compatible API through its `IRecognitionProvider` port (see [Reading photos with an OpenAI-compatible model](photo-reading.md)).

Backend dependencies point inward: Api -> Application, Infrastructure; Infrastructure -> Application; Application -> Domain. Application defines interfaces (ports) that Infrastructure implements, so Application and Domain never reference EF Core or HotChocolate.

The frontend tooling (`package.json`, `vite.config.ts`, `tsconfig*`) lives at the repo root so that both `src/frontend` and `tests/frontend` resolve the same `node_modules`.

## Running the app

Run both in separate terminals:

```sh
mise run dev:api   # API with hot reload on http://localhost:5080 (GraphQL IDE at /graphql)
mise run dev:web   # Vite dev server, proxies /graphql, /auth/oidc/, /media and /imports to :5080
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
  ui/                        the theme, forms, dialogs, toast and other shared building blocks
```

A new test goes into the folder of the feature it covers; run one file with `npx vitest run tests/frontend/<area>/<file>`.

## Data model and migrations

A vehicle fuel log: `Vehicle` (name, licence plate, fuel type, units, optional picture) and `Refueling` (date, volume, a `Cost`, an `OdometerReading`, full tank, whether a fill-up before it was missed, optional note, who logged it). `OdometerReading`, `Cost` and the units are shared building blocks meant to be reused by later entities (inspections, service fees). Exposed via GraphQL (`vehicles`, `vehicle(id)`, `refuelings(vehicleId, ...)`, `refuelingTrash`, `vehicleLogAccess`, `shareCandidates`, `logDefaults`; mutations such as `addVehicle`, `logRefueling`, `updateRefueling`, `deleteRefueling`, `restoreRefueling`, `setVehicleLogAccess`, `emptyTrash`, `emptyRefuelingTrash`).

Adds can carry an id chosen by the client (`id` on `AddVehicleInput`, `LogRefuelingInput`, `AddExpenseInput`, `AddRecurringExpenseInput`; `expenseId` on `MarkRecurringExpensesDoneInput`): sending the same add again, after an answer that never arrived, answers with what the first one created instead of creating it twice (an id someone else used is refused with `sync.idTaken`). The app's add dialogs choose one per opening, so pressing Save again after a network error never logs a refuelling twice. Vehicles, refuelings, expenses and recurring expenses also count their saves (`version`, from 1); this is the groundwork for working offline and syncing later, where a change made from an old version is detected instead of silently overwriting what someone else saved.

What a repeated add does, in detail:

- It answers with **what the first save stored**: values sent again are not compared, so an edit made between a lost answer and the retry is not applied (edit the entry afterwards). Steps after the save that may not have finished (attaching the photos still waiting as drafts, taking a photo reading, recalculating the consumption) run again, since they are safe to repeat.
- The id is looked up across the whole instance, so a user who sends an id **someone else** already used gets `sync.idTaken`; that tells them such an id exists. Ids are random UUIDs, so this cannot be used to discover anything; it is the one place where something invisible does not simply look missing.
- *Mark as done* is a repeat only when its expense id is already linked to **every** schedule asked for; an id linked to other schedules is another visit and is refused (`sync.idTaken`). A *Mark as done* without an amount logs no expense and has no id: sending it again starts the same interval again, which changes nothing but the version.
- Every update or trash is a conditional write: it is saved only if the stored row still has the version the entity was loaded with (`Version` is a concurrency token; `SyncInterceptor` supplies the loaded version), so two changes made from the same version cannot both be saved; the loser gets `sync.versionMismatch`, also online.

What devices download for offline use (the server side; the app's side comes with the next steps):

- `updatedAt` on vehicles, refuelings, expenses and recurring expenses marks anything a device must download again, unlike `version`, which only counts what a writer can conflict with. It is set on every save by one EF interceptor (`SyncInterceptor`), and by the few writes that bypass it: the consumption recalculation, a log's photos added or removed, a schedule's changes for the expenses that list it, and a user's data moved to someone else. Rows saved before it existed carry 2000-01-01.
- Removing for good (an emptied trash, a deleted schedule, a purged vehicle or user) leaves a tombstone (`Tombstones`: the entity's type and id, its vehicle, when), written in the same save by the same interceptor; a vehicle's stands for everything below it. They are kept for `Sync:TombstoneRetentionDays`.
- `offlineChanges(input: { vehicleId, from, since, after, take })` pages through a vehicle (View on its logs). A first download (no `since`) brings the logs dated from `from` (all of them without it), the trash included for those who may change the logs (anyone who may only view them gets a log in the trash as `removed`, as online they do not see the trash); a later one (`since` = the `watermark` the previous download returned) brings every row saved since then, minus a two-minute overlap for saves that committed late, whatever its date, plus `removed`. Refuelings and expenses are paged each by its own key, `(updatedAt, id)` in the database's own order of ids (providers order uuids differently; the database both orders and compares, a test prints every provider's SQL), inside one opaque `next`. The vehicle comes with every page, all its schedules (with today's status) with the first. A `since` older than the tombstones kept answers `resync: true`.
- `offlineSettings` / `updateOfflineSettings` keep the user's offline window with the account (a default and per-vehicle rules: `none`, `all`, `thisYear`, `thisAndLastYear`, `from:yyyy-MM-dd` or `span:` an ISO 8601 duration such as `P2Y6M4DT2H48M12S`; default `span:P2M`); a device turns a rule into a date on its own clock. `Vehicle.logCountSince(from)` counts what a download would bring.

Sending the changes a device kept: `syncChanges(input: { changes: [ChangeInput] })` (1 to 200 per request, in the order they were made). A `ChangeInput` is the change's own id, the version it was made from (`expectedVersion`) and exactly one operation, with the same input as the single mutation (`logRefueling`, `updateExpense`, `deleteVehicle`, `markRecurringExpensesDone`, ...; an add names its id). Each change goes through the same service as its mutation (every rule and access check) and is answered `APPLIED` (with the entity and its new version) or `PARKED` (with the error key and arguments the mutation would have given); the server keeps both in `SyncChanges`, so a change sent again (an answer that never arrived) is answered with what happened the first time and applied once. Applied records go after `Sync:RetentionDays`; parked ones stay for a person to decide. One batch of a user at a time.

Pending migrations are applied automatically at startup. Each provider has its own migration project, so after changing an entity run:

```sh
mise run db:migration AddSomething   # adds the migration to all four providers
```

A unit test fails if any provider's migrations are out of sync with the model.

## GraphQL types are generated

Nothing GraphQL is typed by hand. `schema.graphql` is exported from the API (`mise run schema:export`) and, with the operations in `src/frontend/graphql/*.graphql`, GraphQL Code Generator writes `src/frontend/gql/generated.ts`. After changing the API schema or a `.graphql` document run `mise run codegen` and commit the results; `mise run test` (and an API test) fail if they are stale.

## Import formats

Importing lives in `Application/Imports`: one `IImportParser` per file format turns an uploaded file into an `ImportBatch` (vehicle, fuel logs, expenses, recurring reminders), and `ImportService` keeps the batch in memory for the preview and commits it row by row through the same services the dialogs use, so every rule applies and a bad row is reported rather than fatal. `FuelioCsvParser` is the first format. A new format is a new parser with its own tests, and its name added to `FORMATS` in `src/frontend/pages/ImportPage.tsx` (with the `import.file.formats.<name>` texts in both languages).

## Languages

The UI is in English and Hungarian (react-i18next). Every visible string is in `src/frontend/i18n/locales/<lang>.json` (typed keys; a test checks that all languages have the same messages and placeholders). API errors carry a stable `key` and `args` (e.g. `password.tooShort`, `{min: 10}`) which the UI translates; the API's English message is only a fallback. Dates and numbers use `Intl` for the selected language. To add a language: add `<lang>.json`, list it in `LANGUAGES` (`i18n/index.ts`), and the test tells you what is missing.

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

## Continuous integration

`.github/workflows/ci.yml` runs on every push to `main` and every pull request, installing the toolchain from `mise.toml` so CI uses the same Node and .NET versions as developers. Jobs run in parallel: **frontend** (lint, typecheck, Vitest, production build), **backend** (build, unit and in-process integration tests), **smoke** (the real app as a process in every authentication mode, plus the black-box API contract tests), **codegen** (`schema.graphql` and the generated GraphQL types are up to date). When all pass, **build** uploads the self-hostable output (`out/`) as an artifact. Run the same things locally with `mise run test`.

**Coverage.** The frontend job runs Vitest with V8 coverage (`lcov`) and the backend job runs the unit and integration tests with coverlet (`coverlet.runsettings`: the app's own code, without migrations, the seeder and the tests; Cobertura output). Both upload to [Codecov](https://codecov.io/gh/carathorys/tankstat) under the flags `frontend` and `backend`; `codecov.yml` holds the gates (project coverage may not drop by more than 1% against the base, new and changed lines need 80%) and the pull request comment. The upload needs the Codecov GitHub app and a `CODECOV_TOKEN` repository secret (`release.yml` passes secrets on to `ci.yml`); without the token the step only warns. `mise run test:coverage` writes the same reports to `coverage/` (HTML for the frontend at `coverage/frontend/index.html`).
