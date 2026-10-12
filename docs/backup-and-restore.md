# Backup and restore

For operators: what a backup of a Tankstat instance is, how to make one, how to keep it safe, and how to get the data out of it again.

An instance's data is spread over the database (SQLite, PostgreSQL, SQL Server or MySQL), the uploaded pictures and photos (`Storage:Path`, and `Storage:PhotosPath` where set) and the keys that protect the sign-in cookies and the stored refresh secrets (`DataProtection:KeysPath`). A backup is one file with all of it, taken from one consistent read of the database (a live SQLite file is copied with its backup API, never with `cp`; a server database is read inside one transaction that sees one snapshot: `REPEATABLE READ` on PostgreSQL and MySQL; on SQL Server `SNAPSHOT` isolation where the database allows it, which it does not by default (`ALTER DATABASE <name> SET ALLOW_SNAPSHOT_ISOLATION ON`), else `SERIALIZABLE`, which is just as consistent but makes writers wait while the dump runs, and the log says so). The pictures, photos and keys are copied after the tables, not in the same snapshot: a file uploaded meanwhile is simply not in the backup, or is in it without a row.

A backup in the backups folder is a **restore point**: the instance can be taken back to it at any time (below). Administrators find them on the administration page too ([below](#on-the-administration-page)), and the app can write one every day and remove the old ones ([Schedule and retention](#schedule-and-retention)).

## Making a backup

The commands run inside the application (every setting applies, no server is started), with the same environment the server runs with:

```sh
# Docker: the file lands in /data/backups (a folder of the volume)
docker exec tankstat dotnet Tankstat.Api.dll backup

# without Docker, from the published build, with the server's environment variables set
dotnet out/Tankstat.Api.dll backup
```

`backup` writes `tankstat-backup-<date>T<time>Z.zip` into `Backups:Path` (default `backups` next to the working directory's database; the Docker image uses `/data/backups`) and prints what went in. Options:

| Option | Meaning |
| --- | --- |
| `--output <file>` | write to this file instead (never overwrites an existing one) |
| `--photos` | take the photos of logs along when they have a root of their own (`Storage:PhotosPath`); same as `Backups:Photos=true` for this run |
| `--passphrase-stdin` | encrypt this one backup for a passphrase read from the standard input (one line), with a fresh salt; the manifest says how to derive the key again |

The server may run meanwhile. **Run a backup before every upgrade** and keep it until the new release has proved itself.

## What is in the file

The backup is a zip (stored, not compressed) with two entries, readable with any zip tool:

| Entry | Content |
| --- | --- |
| `data.age` (encrypted) or `data.zip` (plain) | the inner archive, see below |
| `manifest.json` | in plain, always: the app version, the database provider and its last migration, when it was written, every table with its row count, which files and keys went in, the sizes, the SHA-256 of the inner archive, and for an encrypted backup the recipient, the salt and the Argon2id parameters. Nothing personal: no names, e-mails or paths |

`unzip -p <file> manifest.json` prints the manifest. The inner archive holds:

| Folder | Content |
| --- | --- |
| `tables/<Table>.jsonl` | every table of the database as JSON Lines: one object per row, the column names as in the database, typed values (strings for ids and dates in ISO 8601 UTC, numbers, booleans, `null`, base64 for binary). Ids and versions are kept as they are, because devices with offline data refer to them; the sync ledger, tombstones, parked changes, sessions and notifications are tables like any other. `__EFMigrationsHistory.jsonl` records which migrations the database had |
| `files/pictures/...` | everything under `Storage:Path`: profile pictures, vehicle pictures, the files from older versions, and the photos of logs when they live there (the default). Never the `drafts` folders (photos of entries not saved yet, which live a day) |
| `files/photos/...` | a photos root of its own (`Storage:PhotosPath`), only with `Backups:Photos=true` or `--photos`: the photos of refuellings and expenses grow without limit, so they are left out unless asked for. Back that folder up with a file tool (restic, rsync) on its own schedule otherwise |
| `keys/*.xml` | the Data Protection keys, **only in an encrypted backup**: the key files hold their key in plain, and with them the stored refresh secrets are readable. A plain backup leaves them out; restored from it, every device signs in again once |

A backup never holds the backups folder itself: `Backups:Path` must be a folder of its own, neither a storage root nor inside one nor holding one (the app refuses to start otherwise).

## Encryption

A backup should be encrypted: the file can then be kept anywhere (another machine, a cloud bucket) and a copy of it is not a copy of your data. The format is [age](https://age-encryption.org) exactly (version 1, one X25519 recipient, ChaCha20-Poly1305 in 64 KiB chunks), so the real tool opens the inner archive without Tankstat: `unzip -p <file> data.age | age -d -i identity.txt > data.zip`.

The key pair comes from a passphrase: Argon2id (64 MiB, 3 passes) of the passphrase and a random salt is the identity (the secret key), its public key the recipient. **The server keeps only the recipient and the salt**, so nothing on the server can read its own backups. Set it up once:

```sh
echo 'correct horse battery staple' | docker exec -i tankstat dotnet Tankstat.Api.dll backup keygen --passphrase-stdin
```

It prints two settings and the identity:

```text
Set these two settings (as environment variables or in the configuration), then every backup is encrypted for this passphrase:
  Backups__Encryption__Recipient=age1...
  Backups__Encryption__Salt=...
The identity below opens every backup made for this recipient, with age (age -d -i) or tankstat decrypt --identity; ...
  AGE-SECRET-KEY-1...
```

Set the two settings (both, or neither) and restart; from then on every `backup` is encrypted for that recipient and the manifest names it. Keep the passphrase: the identity is derived from it and the salt in every manifest, so the passphrase alone is enough. **Losing the passphrase (and the identity) loses the backups, by design**; nothing on the server can recover them. The passphrase is only ever read from the standard input, never taken as an argument (the process list and the shell history would keep it). A new passphrase (`keygen` again, new settings) applies to the backups written after it; the older files stay under their recipient, which their manifest says.

## Opening a backup

```sh
# with the passphrase (the manifest carries the salt and the parameters)
echo 'correct horse battery staple' | dotnet Tankstat.Api.dll decrypt tankstat-backup-20261011T020000Z.zip --output restored --passphrase-stdin

# with the identity keygen printed (a file holding the AGE-SECRET-KEY-1... line; lines starting with # are ignored)
dotnet Tankstat.Api.dll decrypt tankstat-backup-20261011T020000Z.zip --output restored --identity identity.txt

# a plain backup needs no key
dotnet Tankstat.Api.dll decrypt tankstat-backup-20261011T020000Z.zip --output restored
```

`decrypt` checks the manifest and the checksum, derives the identity and checks it against the recipient **before** anything is unpacked (a wrong passphrase is refused at once), then unpacks `tables/`, `files/` and `keys/` into the folder (which must be empty). A backup written by a newer release is refused.

## Restoring a backup

A restore replaces the instance's data **as a whole**, never merges: every table, the pictures (and the photos when the file holds them), and the keys of an encrypted backup. It happens at a start of the app, after the database migrations and before anything else looks at the data, so it works into an empty database as well (a new volume after a disaster, or a move to another machine or database provider: set the new database up, put the backup and the marker in place, start).

**Asking for it.** Either from the app or from the command line; both leave a small marker, `restore.pending`, in the backups folder, and the next start applies it:

- From the app: an administrator (on the **Backups** page of the administration, or through the GraphQL mutation `restoreBackup(input: { file, passphrase | identity })`) names a backup of the folder and gives its passphrase (or the identity). The passphrase is checked against the backup's recipient **before** anything is written, so a wrong one is refused at once; then the server restarts in its own process, as *Restart the server* on the settings page does (the container keeps running, no restart policy is needed), and the restore is applied as it starts. A restart the next start would refuse, because the settings file was changed by hand into one the app cannot start with, is refused as well, and nothing is written. Until that start a notice says a restore is waiting. With sign-in off (`Auth:Mode=None`) there is no administrator, so the one operation that replaces everything is guarded by a **one-time restore code**, written to the server's log at every start (`docker logs tankstat`): whoever reads the log may restore, nobody else.
- From the command line, with the server's environment: `dotnet Tankstat.Api.dll restore <file> [--yes] [--now] [--identity <file> | --passphrase-stdin]`. The file is named as it is in the backups folder. Without `--yes` it says what it is about to replace and asks for `yes` on the standard input (before the passphrase line, when both come from there). Without `--now` it leaves the marker for the next start of the server; **with `--now` it applies the restore right away, which is only for a server that is not running** (two processes on one database would get in each other's way).

**What the start does**, in order, each step logged: reads the manifest and checks the checksum; refuses a backup made at **another migration** than the database is at (restore it with the release that wrote it, then upgrade; the error names both). Migrations are compared by name, not by id: every database provider has its own copy of each migration, with its own timestamp, so a backup made on one provider is the same migration on another; checks the identity against the recipient; writes a **backup of the data about to be replaced** into the backups folder first (the safety net, `safetyNet: true` in its manifest, encrypted like any other backup; the restore does not go on without it); unpacks the file into a temporary folder; loads every table in one transaction (the database is as it was if that fails); replaces the contents of the pictures folder, of the photos folder when the backup holds one, and of the keys folder for an encrypted backup; removes the marker. A refused restore renames the marker to `restore.refused.<time>` and the app starts with its data as it was; a failure after the tables were replaced (the files half copied) renames it to `restore.failed.<time>`, and the safety net is the way back.

**Afterwards.** Everyone is signed out when the keys changed (a plain backup holds none, so the safety net written while backups are not encrypted signs everyone out once more when it is restored). The sign-in sessions of the backup work again with its keys. **Photos the backup does not hold** stay on disk and keep matching their logs (they are never deleted by a restore, also where they live with the pictures); a photo of a log the backup does not know is left alone too. A missing photo shows a placeholder, never breaks a page.

**Devices with offline data.** Every device refers to the server's entries by their ids and versions; after a restore the server's history is another one (changes made after the backup are gone, versions moved back). The server tells every session which history its data belongs to (a *generation* that the restore renews), and a device that sees a new one sets what it holds aside, drops its downloaded data and kept screens, and downloads afresh. Its changes still waiting are sent as they are and apply or park by the normal rules, so nothing is silently lost; what it held and the server no longer has shows under **Found on this device** on Waiting to sync, where each entry can be sent to the server again or forgotten (see [Install as an app](install-as-an-app.md)).

## On the administration page

The **Backups** page of the administration (`/admin/backups`, in the menu's Administration group; administrators only; with sign-in off there is none, so the commands above are the way) shows:

- **This server**: the backups folder in full (where to copy a file in or out), the size of the database and the migration it is at, the size of the pictures and of the photos (when they have a folder of their own), whether backups are encrypted and for which recipient, whether they take the photos along, the schedule with the next run, and what retention keeps.
- **Back up now**: the server writes a backup in the background, one at a time (a second press while one is being written waits for that one), with the settings above, so encrypted and with the photos exactly as the command would; the tab follows it and says when it is written or why it failed. Leaving the page does not stop it.
- **Upload a backup**: a backup file of another server, or one downloaded earlier, up to `Backups:MaxUploadMb` (1024 MB by default). It is streamed to disk, checked (its manifest and its checksum) and stored as `uploaded-<its name>.zip`, never over another file; anything that is not a backup, or is larger, is refused and leaves nothing behind. A proxy in front of the server may have a limit of its own (nginx: `client_max_body_size`). A larger file can always be copied into the folder by hand.
- **Backups in the folder**: every file in the folder whose manifest can be read, newest first, also one copied in by hand: when and by which release and database it was made, how many rows, whether it is encrypted (for the current key or another one), whether it holds the photos, its size, and whether it is the safety net of a restore or an upload. **Download** saves it; **Restore** asks for the passphrase (or the identity) of an encrypted backup and then restores it as described above. A backup made at another migration has no Restore button: the row names the release that can restore it. After a restore the tab waits for the server to come back and reloads the page once it serves the restored data; a server that comes back without the restore (it was refused) is said so, the log says why.
- **Make a key set**: the same as `backup keygen`, on the server: a passphrase you type (at least 12 characters) or one it makes up (five groups of four letters and digits), the identity, and the two settings to set (`Backups__Encryption__Recipient`, `Backups__Encryption__Salt`). Nothing is stored or logged: it is shown once, and backups are encrypted for it only once those settings are set and the server restarted.

## Schedule and retention

**The schedule.** With `Backups:Every` set to a time of day (`Backups__Every=02:00`), the app writes a backup every day at that time, with the settings above, exactly as **Back up now** does and through the same queue: a backup due while one is being written is that one, never a second. The time is the server's local time; a container runs in UTC unless it is given a time zone (`-e TZ=Europe/Budapest`). On the night the clocks skip that time (02:30 on the spring night in much of Europe), the backup is written the moment the clocks jump; on the night they repeat it, at the first of the two. Unset (the default), nothing is scheduled and the app says nothing about it.

**A missed backup.** When the app starts and the last due time passed without a backup since (it was down at 02:00), it writes that one at once, then goes on by the schedule. A new installation with a schedule writes its first backup at its first start that way, and so does the first start after a restore (the restored backup is older than the last due time); both are this rule, not a fault. Safety nets and uploads do not count as the night's backup.

**Retention.** After every backup the app writes (scheduled or Back up now, not the `backup` command), `Backups:Keep` and `Backups:KeepDays` remove the old ones. Both are ceilings: a backup is removed when it is beyond the newest `Keep` or older than `KeepDays`, whichever are set, and the newest one never goes. Only the app's own files are candidates: named `tankstat-backup-...` by it and written by Tankstat, never an upload (`uploaded-...`) or a file copied into the folder under another name. The safety nets a restore writes are kept by a count of their own: the newest three. With neither setting, nothing is ever removed, safety nets included; a schedule without either makes the app warn at every start, since the folder then grows until the disk is full. A backup that fails removes nothing.

**When a backup fails**, every administrator who is not disabled gets a notification (with sign-in off, the anonymous user), once a day at most however often it fails that day; the administrator who pressed Back up now sees the reason on the page instead. The reason is also in the server's log, and the Backups page says why the last run failed.

## Settings

Every one of them but the folder can be changed on the *Server settings* page too (its **Backups** tab, linked from the Backups page) and applies at once: a new time of day is planned as soon as it is saved, and the next backup takes the new encryption, photos and retention; a backup missed while the app was down is caught up only at its start. *Make a key set* on the Backups page saves its recipient and salt there with one button. An environment variable wins over the page, which then shows the setting read-only.

| Setting | Environment variable | Meaning |
| --- | --- | --- |
| `Backups:Every` | `Backups__Every` | a time of day (`HH:mm`, the server's local time) to write a backup every day; unset, nothing is scheduled |
| `Backups:Keep` | `Backups__Keep` | after each backup the app writes, keep only the newest this many of its own (at least 1); unset, no limit by count |
| `Backups:KeepDays` | `Backups__KeepDays` | after each backup the app writes, remove its own older than this many days (the newest stays); unset, no limit by age |
| `Backups:MaxUploadMb` | `Backups__MaxUploadMb` | the largest backup the administration page uploads, in MB (default 1024); a larger one goes into the folder by hand |
| `Backups:Path` | `Backups__Path` | where backups are written (default `backups`, relative to the working directory; the Docker image uses `/data/backups`, writable by its user). A folder of its own: not a storage root, not inside one, not holding one |
| `Backups:Photos` | `Backups__Photos` | `true` takes a photos root of its own (`Storage:PhotosPath`) into every backup (default `false`: left out, as they grow without limit). No meaning while the photos live with the pictures: they are always in the file then |
| `Backups:Encryption:Recipient` | `Backups__Encryption__Recipient` | the age recipient backups are encrypted for, as `backup keygen` prints it; unset, backups are written in plain, without the keys |
| `Backups:Encryption:Salt` | `Backups__Encryption__Salt` | the salt the identity is derived with (printed with the recipient); set together with it, or neither |

## Moving an instance

A restore is also the move: on the new machine (or the new database provider, since the tables are loaded through the same typed path on every provider), set the same release up with its database, put the backup into its backups folder with the marker (`dotnet Tankstat.Api.dll restore <file> --yes --passphrase-stdin`, or `--now` before the first start), and start. The photos not in the backup go with a file tool.

Every direction between SQLite, PostgreSQL, SQL Server and MySQL has been tried this way, with a database filled by the seeder (28 tables, nearly two thousand rows) and compared table by table after the move. What changes on the way: **timestamps are kept to the microsecond on PostgreSQL and MySQL** (SQLite and SQL Server keep a tenth of that, so the last digit of a timestamp moved to PostgreSQL or MySQL is dropped), and numbers keep their value but may get a different number of trailing zeros. The release has to be the one that wrote the backup (or the same migration of it), whatever the provider on either side.
