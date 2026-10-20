# Contributing to Agenda

Bug reports, focused fixes, documentation and accessible native UI improvements
are welcome. Follow the [code of conduct](CODE_OF_CONDUCT.md). Discuss substantial
features, dependencies and changes to privacy or notification behavior first.

Read [README.md](README.md), [DEVELOPERS.md](DEVELOPERS.md) and
[ARCHITECTURE.md](ARCHITECTURE.md). Use synthetic calendars and fake providers;
real authentication is performed manually by the user. Never commit secret ICS
URLs, credentials, calendar caches, private event content or signing keys.

Keep changes focused and preserve per-user data protection, reminder timing,
provider separation and the native Windows experience. Run the relevant checks
and the standard verification for application changes:

```powershell
./scripts/verify.ps1 -Configuration Release -Architecture x64
```

Record exact commands/results and remaining manual checks. Synthetic tests do not
prove real provider behavior or installed lifecycle acceptance. For documentation
changes, check links and run git diff --check. New dependencies require live
registry/upstream/advisory/license review including transitives and native files;
see [the developer guide](DEVELOPERS.md).

Use the PR template to describe the problem, behavior, verification and limits.
Update affected documentation and the Unreleased changelog when applicable.
Do not incidentally change product versions, tags, dependency pins or generated
build output. Use sanitized screenshots and reproduction data.

Contributions use [GNU AGPL version 3](LICENSE). Preserve applicable copyright,
[third-party notices](THIRD_PARTY_NOTICES.md) and adapted-code provenance.
No contributor agreement is required.
