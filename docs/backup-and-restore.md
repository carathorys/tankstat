# Backup and restore

For operators: what a backup of a Tankstat instance is, how to make one, how to keep it safe, and how to get the data out of it again.

An instance's data is spread over the database (SQLite, PostgreSQL, SQL Server or MySQL), the uploaded pictures and photos (`Storage:Path`, and `Storage:PhotosPath` where set) and the keys that protect the sign-in cookies and the stored refresh secrets (`DataProtection:KeysPath`). A backup is one file with all of it, taken from one consistent read of the database (a live SQLite file is copied with its backup API, never with `cp`; a server database is read inside one transaction that sees one snapshot: `REPEATABLE READ` on PostgreSQL and MySQL; on SQL Server `SNAPSHOT` isolation where the database allows it, which it does not by default (`ALTER DATABASE <name> SET ALLOW_SNAPSHOT_ISOLATION ON`), else `SERIALIZABLE`, which is just as consistent but makes writers wait while the dump runs, and the log says so). The pictures, photos and keys are copied after the tables, not in the same snapshot: a file uploaded meanwhile is simply not in the backup, or is in it without a row.

> **Restore is not there yet.** This page describes the file and the commands that write and open it. Restoring a backup into an instance (the whole-instance replace, the restore points on the administration page, the schedule and the retention) follows in the next releases; until then a backup is the data in a form you can read without Tankstat, and the way to move an instance by hand.

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

## Settings

| Setting | Environment variable | Meaning |
| --- | --- | --- |
| `Backups:Path` | `Backups__Path` | where backups are written (default `backups`, relative to the working directory; the Docker image uses `/data/backups`, writable by its user). A folder of its own: not a storage root, not inside one, not holding one |
| `Backups:Photos` | `Backups__Photos` | `true` takes a photos root of its own (`Storage:PhotosPath`) into every backup (default `false`: left out, as they grow without limit). No meaning while the photos live with the pictures: they are always in the file then |
| `Backups:Encryption:Recipient` | `Backups__Encryption__Recipient` | the age recipient backups are encrypted for, as `backup keygen` prints it; unset, backups are written in plain, without the keys |
| `Backups:Encryption:Salt` | `Backups__Encryption__Salt` | the salt the identity is derived with (printed with the recipient); set together with it, or neither |

## Moving an instance by hand, for now

Until the restore command exists, a backup gets an instance's data out in a form any tool reads: the tables as JSON Lines, the files as they were on disk. To move an instance to another machine with the same database provider, copy the database (SQLite: the file, after stopping the app; a server database: its own dump and restore), the storage folders and the keys folder, and start the same release there; the backup is the safety net for that. Moving between providers goes through the restore, which is still to come.
