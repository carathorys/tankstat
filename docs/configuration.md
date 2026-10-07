# Configuration

For operators: the database, uploaded files, defaults, notification limits and the log. Authentication has [a page of its own](authentication.md).

Every setting below can be given in `appsettings.json` or as an environment variable, where `:` becomes `__` (double underscore); environment variables override the file (the standard ASP.NET Core order: appsettings.json < appsettings.{Environment}.json < environment variables < command line). The same holds for the [authentication](authentication.md) and [photo reading](photo-reading.md) settings.

## Database

The database is chosen through the `Database` section. If nothing is configured, the app falls back to a SQLite file, `tankstat.db`, in the working directory.

| Setting | Environment variable | Values |
| --- | --- | --- |
| `Database:Provider` | `Database__Provider` | `Sqlite`, `PostgreSql`, `SqlServer`, `MySql` (Oracle provider; MariaDB is not a tested target) |
| `Database:ConnectionString` | `Database__ConnectionString` | provider-specific connection string |

```sh
Database__Provider=PostgreSql Database__ConnectionString="Host=db;Database=tankstat;Username=app;Password=..." \
  dotnet out/Tankstat.Api.dll
```

The connection string may be omitted only for `Sqlite`. The app refuses to start if it is missing for another provider, or if the provider is unknown.

## Files, defaults and notifications

Uploaded pictures (profile pictures, vehicle pictures, photos of refuelings and expenses) are stored as files; only metadata is in the database:

| Setting | Environment variable | Meaning |
| --- | --- | --- |
| `Storage:Path` | `Storage__Path` | folder for uploaded pictures (default `uploads`, relative to the working directory; the Docker image uses `/data/uploads`). Files are organised per owner: `users/<id>/` for avatars and `vehicles/<id>/` for everything of a vehicle (its picture and, below it, the photos of its logs and the photo drafts of entries not saved yet), so a vehicle's files are removed with it; files from older versions stay directly in the folder and keep working |
| `Defaults:DistanceUnit`, `Defaults:VolumeUnit`, `Defaults:Currency` | `Defaults__...` | what a new vehicle / new log starts with (`Kilometers` / `Liters` / `EUR` unless changed) |
| `Defaults:RecurringWarnDays`, `Defaults:RecurringWarnDistance` | `Defaults__...` | how early a new recurring expense starts warning (`30` days / `500` in the vehicle's distance unit unless changed; every schedule can override it) |
| `Notifications:MaxPerHour` | `Notifications__MaxPerHour` | how many notifications about events (such as access changes) a user gets in an hour before further ones are only counted (default `20`, at least `1`; recurring-expense reminders do not count) |
| `Notifications:ReadRetentionDays` | `Notifications__ReadRetentionDays` | how many days a notification is kept after it was read, then it is removed (default `30`, at least `1`; unread ones are kept) |

## Logging

The app writes its log to the console (`docker logs`, the terminal) with the standard .NET logger; there is nothing to install. The shipped `Logging` settings in `appsettings.json` keep the app's own lines (category `Tankstat`) at `Information` and silence what used to drown them: Entity Framework's SQL commands and the HTTP client's per-request lines.

| Level | What is logged |
| --- | --- |
| `Error` | an unexpected error, with its exception and stack trace (the client only sees "Unexpected Execution Error") |
| `Warning` | a refused request (no access), a failed sign-in, a failed password change, a lockout, a refused sign-in through an identity provider, a malformed or untrusted proxy header, a database or model server that cannot be used, an upload folder that could not be removed, a photo that could not be attached |
| `Information` | start-up (database migrations applied or up to date, the first administrator), sign-ins and sign-outs, password changes and reset requests, what an administrator does to users and to access, sharing a vehicle's logs, imports, emptied trashes, which model and server photo reading uses and where its system prompt comes from, a database or model server that is back |
| `Debug` | ordinary changes (a vehicle, refueling, expense, schedule, chart, picture or photo added, changed, trashed or restored), every GraphQL request with its time and operation, errors a client causes (a validation failure, something missing, a request the server turns down), sessions that are no longer valid and why, and the trail of each photo a model reads (see [Reading photos with an OpenAI-compatible model](photo-reading.md#following-a-photo-through-the-log)) |

**Only ids, counts and reasons are logged** by the app's own lines: users, vehicles, logs and pictures by id, never e-mail addresses, names, license plates, notes, amounts, odometer readings, passwords, reset links, API keys, the subject an identity provider sends, or values read from photos (why a value was dropped is logged as a code such as `OdometerBelowLatest`, never the value). The one exception is opt-in: `Recognition__OpenAiCompatible__LogTraffic=true` also writes the prompts and what a model read, never the photo or the key, to debug a model ([details](photo-reading.md#seeing-what-is-sent-to-the-model)). A failed sign-in names the account by its id (or says "unknown account"), not by the address that was typed. Nothing a client sends is logged as it came: not GraphQL variables or documents, not the text of a request error, fields are named the way the schema names them, and the endpoints of the picture and import uploads appear as their route pattern, not as the path that was asked for.

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
