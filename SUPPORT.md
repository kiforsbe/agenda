# Getting help

Read [README.md](README.md) for calendar/feed/reminder usage and
[DEVELOPERS.md](DEVELOPERS.md) for build and package troubleshooting. Search
existing issues before using the bug or feature-request template. Support has
no guaranteed response time; do not assume a private support channel exists.

Include Agenda's version/revision, Windows build, architecture, MSI/MSIX/portable
format, expected behavior and minimal steps using synthetic calendar events.
For UI issues include theme and scaling. For reminders include the selected
timing/sound and whether Windows notification settings or Do Not Disturb apply.

Remove secret ICS links, tokens, account identifiers, personal paths and event
details from logs and captures. Do not attach settings/cache directories,
personal ICS files or signing keys. Suspected vulnerabilities follow
[SECURITY.md](SECURITY.md).

Unsigned MSI may be blocked by reputation or managed-device policy. Development
MSIX requires deliberate trust of the matching public certificate; never import
an unknown certificate to work around an installation problem.
