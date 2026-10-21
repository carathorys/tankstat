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

**A dependency's published CVE** that Tankstat can be attacked through (you can show how) is a vulnerability like any other: report it privately, as above. A CVE in a dependency that the app does not reach needs no report: Dependabot's security updates raise those on their own, in the open.

Not a vulnerability: running with `Auth:Mode=None` (the shipped `appsettings.json`), which the documentation and the app's own notice say makes everything visible to everyone; and what someone can do only with powers the app gives them anyway, an administrator's in the app or the host's over its files, settings and processes. What those powers must not reach is in scope all the same: a backup's encryption against someone holding only the file, an administrator reaching another server's data or the host through the app.
