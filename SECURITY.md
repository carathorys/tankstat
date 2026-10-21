# Security

## Reporting a vulnerability

Please do not open a public issue for a security problem. Report it privately through GitHub's [Report a vulnerability](https://github.com/carathorys/tankstat/security/advisories/new) form (the *Security* tab of the repository). Say which version is affected (it is in the footer of the app), how the app runs (the Docker image or a published build), the sign-in mode, and the steps that show the problem.

You get an acknowledgement within a week. A confirmed problem is fixed in a patch release of the current minor, with an advisory and a CHANGELOG entry that credits you unless you prefer not to be named.

## Supported versions

Tankstat is at `0.x`: only the latest minor release and its patches receive fixes. Upgrade to the current `0.N` tag (or `latest`) before reporting; the [CHANGELOG](CHANGELOG.md) says what each release changed.

## What counts

Anything that lets a person see or change data they were not given access to, or that weakens what the documentation promises:

- the sign-in modes (Standalone accounts, OpenID Connect, the trusted reverse-proxy header), sessions and refresh tokens, password resets, the setup page and its one-time code;
- access control: owners, administrators, grants, sharing a vehicle's logs, the trash;
- backups: their encryption, the keys they carry, what a restore can change;
- the files the app stores and serves (pictures, photos, uploads);
- the photo-reading integration (where the API key goes, what is sent to the model server);
- the offline data on a device and how changes are sent back.

Not a vulnerability: running with `Auth:Mode=None` (the shipped `appsettings.json`), which the documentation and the app's own notice say makes everything visible to everyone; and anything that needs an administrator's account or the host to begin with. Reports of a dependency's published CVE are welcome as ordinary issues when the app is affected.
