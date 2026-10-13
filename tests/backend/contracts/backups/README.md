# Backup fixtures

Backups of an older release, which every later release must still restore (`UpgradeTests` in `Tankstat.Api.IntegrationTests`). Never
regenerate one for a new release: an older release's file is the point.

- `older-migration-AddExpenses.zip`: a plain SQLite backup made at the migration `AddExpenses` (before the stored fuel consumption), with
  sign-in off: one vehicle ("Golf from before"), two full fill-ups 500 km apart (40 and 30 litres) and one expense ("Wash"). Restored,
  it is brought up to date, and the second fill-up's consumption (6 l/100 km) is worked out on the way. No user, nothing personal.

Written by `UpgradeTests.TheFixture_IsThere_OrIsWrittenWhenAskedFor` with `TANKSTAT_WRITE_BACKUP_FIXTURE=1` (the data made through the
API, the database taken back by the migrations' own Down steps, then backed up as it is there).
