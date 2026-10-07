# Importing

For people who bring their logs from another app.

The *Import* page walks through four steps: choose the file, choose the target (an existing vehicle you may add logs to, or a new one), review, done. Nothing is saved before you confirm.

- The file is uploaded raw with `POST /imports/{format}` (the answer is a token for the parsed file, kept in memory for 30 minutes); the preview (`importPreview`) and the confirmation (`confirmImport`) are GraphQL.
- **Fuelio** (`fuelio`): the "sync" CSV export of one vehicle (unzip it first). Fuel logs, other costs (with their category names) and the vehicle's name, plate, units and fuel type are read; reminder templates that repeat (every so many months and/or kilometres) become recurring expenses, counting from where the reminder stands now (their amount is not kept; one-off reminders are skipped), a log Fuelio marks as following a missed fill-up keeps that mark, stations, GPS and weather are ignored, income rows are skipped, and a cost with odometer 0 gets no odometer. Fuelio files carry no currency, so you enter one (it defaults to `Defaults:Currency`). Dates keep only the day.
- Rows are saved through the same services as rows typed by hand, so every rule applies (access, odometer order against the vehicle's other readings, future dates). A row that breaks a rule is reported and the rest is still imported. For an existing vehicle you choose whether rows that already exist (same date and odometer; same date, title and amount for expenses) are skipped or imported again.
- More formats (other CSV or JSON sources) are one more `IImportParser` that turns a file into an `ImportBatch` (see [Import formats](development.md#import-formats)).
