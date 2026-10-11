# age fixtures

Files written by [age](https://age-encryption.org) 1.3.2, so that Tankstat's own age reader and writer are checked against the real tool,
not only against each other (`AgeStreamTests` in `Tankstat.Infrastructure.UnitTests`):

- `identity.txt`, `recipient.txt`: a key pair from `age-keygen` (a test key, used for nothing else); `other-identity.txt` another one, which opens nothing here.
- `plain.txt`, `plain.txt.age`: a short text and `age -r <recipient>` of it (one chunk).
- `plain-70000.bin`, `plain-70000.bin.age`: 70 000 random bytes, encrypted the same way (a full 64 KiB chunk and a final one).
- `passphrase.age`: `plain.txt` encrypted with `age -p` (passphrase `hunter2`), which a backup reader refuses: a backup is always encrypted for a recipient.

Made with `mise exec age@1.3.2 -- age ...`; `age -p` needs a terminal (`script -qefc "..." /dev/null`).
