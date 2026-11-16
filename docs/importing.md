# Importing

For people who bring their logs from another app.

The *Import* page walks through four steps: choose the file, choose the target (an existing vehicle you may add logs to, or a new one), review, done. Nothing is saved before you confirm.

- The file is uploaded raw with `POST /imports/{format}` (the answer is a token for the parsed file, kept in memory for 30 minutes); the preview (`importPreview`) and the confirmation (`confirmImport`) are GraphQL.
- **Fuelio** (`fuelio`): the "sync" CSV export of one vehicle (unzip it first). Fuel logs, other costs (with their category names) and the vehicle's name, plate, units and fuel type are read; reminder templates that repeat (every so many months and/or kilometres) become recurring expenses, counting from where the reminder stands now (their amount is not kept; one-off reminders are skipped), a log Fuelio marks as following a missed fill-up keeps that mark, stations, GPS and weather are ignored, income rows are skipped, and a cost with odometer 0 gets no odometer. Fuelio files carry no currency, so you enter one (it defaults to `Defaults:Currency`). Dates keep only the day.
- Rows are saved through the same services as rows typed by hand, so every rule applies (access, odometer order against the vehicle's other readings, future dates). A row that breaks a rule is reported and the rest is still imported. For an existing vehicle you choose whether rows that already exist (same date and odometer; same date, title and amount for expenses) are skipped or imported again.
- More formats (other CSV or JSON sources) are one more `IImportParser` that turns a file into an `ImportBatch` (see [Import formats](development.md#import-formats)).
- **A Tankstat backup or export**: cars out of a backup file of this app, or of an export made on its Import and export page (`.zip`). See [Cars from a backup](#cars-from-a-backup).

## Cars from a backup

Choose *Tankstat backup or export* as the source on the Import page (with `Imports:BackupEnabled` turned off it is offered to administrators only, and with sign-in off to nobody).

1. **File.** Upload the backup (up to `Imports:MaxUploadMb`, 200 MB by default). An encrypted one asks for its passphrase: three tries, then the file is removed from the server; the passphrase is used for that try and kept nowhere. The file waits on the server for 30 minutes after its last use, one file per person, and for as long as an import of it waits or runs.
2. **Cars.** The file's cars, each with whose it was and how much it holds. Tick the ones you want. A car comes **as a new car**, or is **added to one of your own cars** (the app suggests one when the name or the license plate matches). A car can only be added to a car in the same units; numbers are never converted. Cars that were in the backup's trash are left out.
3. **Review.** Per car: what is new, what already exists and is skipped, what differs from yours and stays as it is, and what is in your car's trash (not imported; restore it there if you want it). For each car you choose what happens when an entry cannot be saved: *Import the rest* (the default; the entry is listed in the report), *Leave this car out*, or *Stop the whole import*.
4. **Done.** The report: per car what came in, what was skipped, and each entry that could not be saved with its reason.

What you import **becomes yours**. Sharing, who logged an entry and marks for review are not carried over. So far refuellings, expenses and recurring expenses come in: tyres, photos, pictures and saved charts do not yet.

An import is **all or nothing**: when it is cancelled, takes longer than the server allows (`Runs:LimitMinutes`), or the server restarts while it runs, nothing of it is kept and your cars are as they were. While it runs, saving may have to wait for a moment, for you and for others. One import runs at a time on an instance: another waits in line and starts by itself (for up to 30 minutes). You can leave the page; a notification tells you when it is done and leads to the report, which is kept for 7 days. You, or an administrator, can cancel an import that waits or runs; administrators see them on the Overview and the Backups page, and are notified of what was imported.

Importing from a backup does not work on **MySQL** yet.
